using System.Globalization;
using System.Text.RegularExpressions;
using Psique.Api.Data;
using Psique.Api.DTOs.ModelosMensagem;
using Psique.Api.Models;
using Psique.Api.Services.Comunicacao;
using Psique.Api.Services.Seguranca;
using Psique.Api.Shared.Enums;
using Psique.Api.Shared.Excecoes;
using Microsoft.EntityFrameworkCore;

namespace Psique.Api.Services.ModelosMensagem;

public sealed partial class ModelosMensagemService(
    AppDbContext dbContext,
    IAutorizacaoContaService autorizacao,
    IAuditoriaService auditoria,
    IModeloMensagemTemplateService templateService) : IModelosMensagemService
{
    // O lembrete automático por e-mail é o único envio que usa modelos hoje; aceitar outros tipos ou canais
    // deixaria o psicólogo escrever textos que nunca saem.
    private static readonly HashSet<CanalComunicacao> CanaisOperacionais =
    [
        CanalComunicacao.Email
    ];

    private static readonly HashSet<TipoLembrete> TiposOperacionais =
    [
        TipoLembrete.LembreteConsulta
    ];

    public async Task<IReadOnlyCollection<ModeloMensagemResponse>> ListarAsync(
        Guid usuarioId,
        Guid contaId,
        FiltroModeloMensagemRequest filtro,
        CancellationToken cancellationToken)
    {
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, cancellationToken);

        var consulta = dbContext.ModelosMensagem
            .AsNoTracking()
            .Where(modelo => modelo.ContaPsicologoId == contaId);

        if (filtro.Tipo is not null)
            consulta = consulta.Where(modelo => modelo.Tipo == filtro.Tipo);
        if (filtro.Canal is not null)
            consulta = consulta.Where(modelo => modelo.Canal == filtro.Canal);
        if (filtro.Ativo is not null)
            consulta = consulta.Where(modelo => modelo.Ativo == filtro.Ativo);

        var itens = await consulta
            .OrderByDescending(modelo => modelo.Ativo)
            .ThenBy(modelo => modelo.Nome)
            .ToArrayAsync(cancellationToken);

        return itens.Select(Mapear).ToArray();
    }

    public async Task<ModeloMensagemResponse> ObterAsync(
        Guid usuarioId,
        Guid contaId,
        Guid id,
        CancellationToken cancellationToken)
    {
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, cancellationToken);
        var modelo = await ObterModeloAsync(contaId, id, cancellationToken, rastrear: false);
        return Mapear(modelo);
    }

    public async Task<IReadOnlyCollection<VariavelModeloMensagemResponse>> ListarVariaveisAsync(
        Guid usuarioId,
        Guid contaId,
        CancellationToken cancellationToken)
    {
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, cancellationToken);

        return templateService.ListarVariaveisPermitidas()
            .Select(item => new VariavelModeloMensagemResponse(item.Chave, item.Descricao, $"{{{{{item.Chave}}}}}"))
            .ToArray();
    }

    public async Task<ModeloMensagemResponse> CriarAsync(
        Guid usuarioId,
        Guid contaId,
        CriarModeloMensagemRequest request,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, cancellationToken);
        ValidarTipo(request.Tipo);
        ValidarCanal(request.Canal);
        ValidarAssunto(request.Canal, request.Assunto);
        templateService.ValidarConteudo(request.Conteudo, request.Assunto);

        var nome = LimparObrigatorio(request.Nome, "Nome do modelo");
        var jaExiste = await dbContext.ModelosMensagem.AnyAsync(
            modelo => modelo.ContaPsicologoId == contaId &&
                      modelo.Nome == nome &&
                      modelo.Tipo == request.Tipo &&
                      modelo.Canal == request.Canal,
            cancellationToken);
        if (jaExiste)
            throw new RegraNegocioException("Já existe um modelo com este nome, tipo e canal.");

        var modelo = new ModeloMensagem
        {
            Id = Guid.NewGuid(),
            ContaPsicologoId = contaId,
            Nome = nome,
            Tipo = request.Tipo,
            Canal = request.Canal,
            Assunto = Limpar(request.Assunto),
            Conteudo = request.Conteudo.Trim(),
            Ativo = request.Ativo
        };

        dbContext.ModelosMensagem.Add(modelo);
        auditoria.Registrar(
            contaId,
            usuarioId,
            "ModeloMensagemCriado",
            nameof(ModeloMensagem),
            modelo.Id,
            "Modelo de mensagem administrativa criado.",
            null,
            new { modelo.Nome, modelo.Tipo, modelo.Canal, modelo.Ativo },
            ip,
            userAgent);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Mapear(modelo);
    }

    public async Task<ModeloMensagemResponse> AtualizarAsync(
        Guid usuarioId,
        Guid contaId,
        Guid id,
        AtualizarModeloMensagemRequest request,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, cancellationToken);
        ValidarTipo(request.Tipo);
        ValidarCanal(request.Canal);
        ValidarAssunto(request.Canal, request.Assunto);
        templateService.ValidarConteudo(request.Conteudo, request.Assunto);

        var modelo = await ObterModeloAsync(contaId, id, cancellationToken, rastrear: true);
        var nome = LimparObrigatorio(request.Nome, "Nome do modelo");
        var jaExiste = await dbContext.ModelosMensagem.AnyAsync(
            item => item.ContaPsicologoId == contaId &&
                    item.Id != id &&
                    item.Nome == nome &&
                    item.Tipo == request.Tipo &&
                    item.Canal == request.Canal,
            cancellationToken);
        if (jaExiste)
            throw new RegraNegocioException("Já existe um modelo com este nome, tipo e canal.");

        var antes = new { modelo.Nome, modelo.Tipo, modelo.Canal, modelo.Assunto, modelo.Ativo };
        modelo.Nome = nome;
        modelo.Tipo = request.Tipo;
        modelo.Canal = request.Canal;
        modelo.Assunto = Limpar(request.Assunto);
        modelo.Conteudo = request.Conteudo.Trim();
        modelo.Ativo = request.Ativo;
        modelo.AtualizadoEm = DateTimeOffset.UtcNow;

        auditoria.Registrar(
            contaId,
            usuarioId,
            "ModeloMensagemAtualizado",
            nameof(ModeloMensagem),
            modelo.Id,
            "Modelo de mensagem administrativa atualizado.",
            antes,
            new { modelo.Nome, modelo.Tipo, modelo.Canal, modelo.Assunto, modelo.Ativo },
            ip,
            userAgent);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Mapear(modelo);
    }

    public async Task<ModeloMensagemResponse> AlterarAtivoAsync(
        Guid usuarioId,
        Guid contaId,
        Guid id,
        AlterarAtivoModeloMensagemRequest request,
        string? ip,
        string? userAgent,
        CancellationToken cancellationToken)
    {
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, cancellationToken);
        var modelo = await ObterModeloAsync(contaId, id, cancellationToken, rastrear: true);
        if (modelo.Ativo == request.Ativo)
            return Mapear(modelo);

        var ativoAnterior = modelo.Ativo;
        modelo.Ativo = request.Ativo;
        modelo.AtualizadoEm = DateTimeOffset.UtcNow;
        auditoria.Registrar(
            contaId,
            usuarioId,
            request.Ativo ? "ModeloMensagemAtivado" : "ModeloMensagemInativado",
            nameof(ModeloMensagem),
            modelo.Id,
            request.Ativo ? "Modelo de mensagem ativado." : "Modelo de mensagem inativado.",
            new { Ativo = ativoAnterior },
            new { modelo.Ativo },
            ip,
            userAgent);
        await dbContext.SaveChangesAsync(cancellationToken);
        return Mapear(modelo);
    }

    public async Task<PreviaModeloMensagemResponse> PreviaAsync(
        Guid usuarioId,
        Guid contaId,
        PreviaModeloMensagemRequest request,
        CancellationToken cancellationToken)
    {
        await autorizacao.ObterUsuarioAtivoAsync(usuarioId, contaId, cancellationToken);
        templateService.ValidarConteudo(request.Conteudo, request.Assunto);

        var valores = await MontarValoresPreviaAsync(
            contaId,
            request.ConsultaId,
            request.PacienteId,
            cancellationToken);

        var assunto = templateService.RenderizarAssunto(request.Assunto, valores);
        var conteudo = templateService.Renderizar(request.Conteudo, valores);
        var utilizadas = ExtrairChaves(request.Conteudo)
            .Concat(ExtrairChaves(request.Assunto ?? string.Empty))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(chave => chave, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PreviaModeloMensagemResponse(
            string.IsNullOrWhiteSpace(assunto) ? null : assunto,
            conteudo,
            utilizadas);
    }

    private async Task<ModeloMensagem> ObterModeloAsync(
        Guid contaId,
        Guid id,
        CancellationToken cancellationToken,
        bool rastrear)
    {
        var consulta = rastrear
            ? dbContext.ModelosMensagem.AsQueryable()
            : dbContext.ModelosMensagem.AsNoTracking();

        return await consulta.SingleOrDefaultAsync(
                   modelo => modelo.Id == id && modelo.ContaPsicologoId == contaId,
                   cancellationToken)
               ?? throw new RegraNegocioException("Modelo de mensagem não encontrado.", StatusCodes.Status404NotFound);
    }

    private async Task<IReadOnlyDictionary<string, string?>> MontarValoresPreviaAsync(
        Guid contaId,
        Guid? consultaId,
        Guid? pacienteId,
        CancellationToken cancellationToken)
    {
        var cultura = CultureInfo.GetCultureInfo("pt-BR");
        var valores = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [VariaveisModeloMensagem.NomePaciente] = "Paciente exemplo",
            [VariaveisModeloMensagem.DataConsulta] = DateTime.Today.AddDays(1).ToString("dd/MM/yyyy", cultura),
            [VariaveisModeloMensagem.HorarioConsulta] = "14:00",
            [VariaveisModeloMensagem.Modalidade] = "Presencial",
            [VariaveisModeloMensagem.Endereco] = "Endereço administrativo do consultório",
            [VariaveisModeloMensagem.LinkSessaoOnline] = string.Empty,
            [VariaveisModeloMensagem.RegrasCancelamento] = "Cancelamentos com antecedência mínima configurada.",
            [VariaveisModeloMensagem.RegrasRemarcacao] = "Remarcações conforme política do consultório.",
            [VariaveisModeloMensagem.NomeProfissional] = "Profissional"
        };

        var profissional = await dbContext.DadosProfissionaisPsicologo
            .AsNoTracking()
            .Where(item => item.ContaPsicologoId == contaId && item.Status == StatusUsuario.Ativo)
            .OrderBy(item => item.CriadoEm)
            .Select(item => item.NomeProfissional)
            .FirstOrDefaultAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(profissional))
            valores[VariaveisModeloMensagem.NomeProfissional] = profissional;

        var configuracao = await dbContext.ConfiguracoesAtendimento
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.ContaPsicologoId == contaId && item.Ativo, cancellationToken);
        if (configuracao is not null)
        {
            valores[VariaveisModeloMensagem.RegrasCancelamento] = MontarRegraAntecedencia(
                "cancelamento",
                configuracao.AntecedenciaCancelamentoHoras,
                configuracao.ExigeJustificativaCancelamento);
            valores[VariaveisModeloMensagem.RegrasRemarcacao] = MontarRegraAntecedencia(
                "remarcação",
                configuracao.AntecedenciaRemarcacaoHoras,
                false);
        }

        if (consultaId is not null)
        {
            var consulta = await dbContext.Consultas
                .AsNoTracking()
                .Include(item => item.Paciente)
                .SingleOrDefaultAsync(
                    item => item.Id == consultaId && item.ContaPsicologoId == contaId,
                    cancellationToken)
                ?? throw new RegraNegocioException("Consulta não encontrada.", StatusCodes.Status404NotFound);

            PreencherValoresConsulta(valores, consulta, cultura, Agenda.FusoHorarioConta.Resolver(configuracao?.FusoHorario));
            return valores;
        }

        if (pacienteId is not null)
        {
            var paciente = await dbContext.Pacientes
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    item => item.Id == pacienteId && item.ContaPsicologoId == contaId,
                    cancellationToken)
                ?? throw new RegraNegocioException("Paciente não encontrado.", StatusCodes.Status404NotFound);
            valores[VariaveisModeloMensagem.NomePaciente] = paciente.NomeCompleto;
        }

        return valores;
    }

    internal static void PreencherValoresConsulta(
        IDictionary<string, string?> valores,
        Consulta consulta,
        CultureInfo cultura,
        TimeZoneInfo fuso)
    {
        valores[VariaveisModeloMensagem.NomePaciente] = consulta.Paciente.NomeCompleto;
        var inicioLocal = TimeZoneInfo.ConvertTime(consulta.InicioEm, fuso);
        valores[VariaveisModeloMensagem.DataConsulta] = inicioLocal.ToString("dd/MM/yyyy", cultura);
        valores[VariaveisModeloMensagem.HorarioConsulta] = inicioLocal.ToString("HH:mm", cultura);
        valores[VariaveisModeloMensagem.Modalidade] = consulta.Modalidade switch
        {
            ModalidadeAtendimento.Online => "Online",
            ModalidadeAtendimento.Presencial => "Presencial",
            ModalidadeAtendimento.Hibrido => "Híbrido",
            _ => consulta.Modalidade.ToString()
        };
        valores[VariaveisModeloMensagem.Endereco] = consulta.EnderecoAtendimento;
        valores[VariaveisModeloMensagem.LinkSessaoOnline] = consulta.LinkOnline;
    }

    private static string MontarRegraAntecedencia(string tipo, int? horas, bool exigeJustificativa)
    {
        if (horas is null or <= 0)
            return $"Sem antecedência mínima configurada para {tipo}.";

        var baseTexto = $"Antecedência mínima de {horas} hora(s) para {tipo}.";
        return exigeJustificativa
            ? $"{baseTexto} Justificativa obrigatória."
            : baseTexto;
    }

    private static void ValidarCanal(CanalComunicacao canal)
    {
        if (!CanaisOperacionais.Contains(canal))
            throw new RegraNegocioException("Por enquanto, os modelos de mensagem são enviados por e-mail.");
    }

    private static void ValidarTipo(TipoLembrete tipo)
    {
        if (!TiposOperacionais.Contains(tipo))
            throw new RegraNegocioException("Por enquanto, só o lembrete de consulta usa modelos de mensagem.");
    }

    private static void ValidarAssunto(CanalComunicacao canal, string? assunto)
    {
        if (canal == CanalComunicacao.Email && string.IsNullOrWhiteSpace(assunto))
            throw new RegraNegocioException("O assunto é obrigatório para modelos de e-mail.");
    }

    private static string LimparObrigatorio(string? valor, string campo)
    {
        if (string.IsNullOrWhiteSpace(valor))
            throw new RegraNegocioException($"{campo} é obrigatório.");
        return valor.Trim();
    }

    private static string? Limpar(string? valor) =>
        string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    private static IEnumerable<string> ExtrairChaves(string texto) =>
        VariavelRegex().Matches(texto).Select(match => match.Groups["chave"].Value);

    private static ModeloMensagemResponse Mapear(ModeloMensagem modelo) => new(
        modelo.Id,
        modelo.Nome,
        modelo.Tipo,
        modelo.Canal,
        modelo.Assunto,
        modelo.Conteudo,
        modelo.Ativo,
        modelo.CriadoEm,
        modelo.AtualizadoEm);

    [GeneratedRegex(@"\{\{\s*(?<chave>[a-zA-Z0-9_]+)\s*\}\}", RegexOptions.CultureInvariant)]
    private static partial Regex VariavelRegex();
}
