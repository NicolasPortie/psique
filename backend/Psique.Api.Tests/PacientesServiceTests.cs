using Microsoft.EntityFrameworkCore;
using Psique.Api.Data;
using Psique.Api.Models;
using Psique.Api.Services.Pacientes;
using Psique.Api.Services.Seguranca;
using Psique.Api.Shared.Enums;

namespace Psique.Api.Tests;

public sealed class PacientesServiceTests
{
    [Theory]
    [InlineData("ana123@exemplo.com", "ana123@exemplo.com")]
    [InlineData(" ANA123@EXEMPLO.COM ", "ana123@exemplo.com")]
    [InlineData("(11) 99999-1234", "11999991234")]
    public async Task Busca_EncontraContatoExatoSemConfundirEmailComTelefone(string busca, string contato)
    {
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var contaId = Guid.NewGuid();
        var usuario = new Usuario { Id = Guid.NewGuid(), ContaPsicologoId = contaId, Email = "psi@teste.local", SenhaHash = "hash" };
        var paciente = new Paciente { Id = Guid.NewGuid(), ContaPsicologoId = contaId, NomeCompleto = "Paciente da conta" };
        var externo = new Paciente { Id = Guid.NewGuid(), ContaPsicologoId = Guid.NewGuid(), NomeCompleto = "Paciente de outra conta" };
        db.AddRange(new ContaPsicologo { Id = contaId }, usuario, paciente, externo);
        foreach (var item in new[] { paciente, externo })
            db.Add(new ContatoPaciente { Id = Guid.NewGuid(), ContaPsicologoId = item.ContaPsicologoId, PacienteId = item.Id,
                TipoContato = contato.Contains('@') ? TipoContato.Email : TipoContato.Telefone, Valor = $"protegido:{contato}", ValorHash = $"hash:{contato}" });
        await db.SaveChangesAsync();
        var service = new PacientesService(db, new ProtecaoFake(), new AuditoriaFake());
        var resultado = await service.ListarAsync(usuario.Id, contaId, new(busca, null), CancellationToken.None);
        Assert.Equal(paciente.Id, Assert.Single(resultado.Itens).Id);
    }

    private sealed class ProtecaoFake : IProtecaoDadosService
    {
        public string Proteger(string valor) => $"protegido:{valor}";
        public string Desproteger(string valor) => valor.Replace("protegido:", "");
        public string HashDeterministico(string valor) => $"hash:{valor}";
        public string HashToken(string valor) => throw new NotSupportedException();
        public string GerarTokenSeguro() => throw new NotSupportedException();
    }

    private sealed class AuditoriaFake : IAuditoriaService
    {
        public void Registrar(Guid contaPsicologoId, Guid? usuarioId, string tipoAcao, string entidade, Guid? entidadeId, string descricao, object? dadosAntes = null, object? dadosDepois = null, string? ip = null, string? userAgent = null) { }
    }
}
