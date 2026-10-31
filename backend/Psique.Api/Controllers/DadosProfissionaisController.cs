using Psique.Api.DTOs.DadosProfissionais;
using Psique.Api.Services.DadosProfissionais;
using Psique.Api.Shared.Respostas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Psique.Api.Controllers;

[ApiController]
[Authorize(Policy = "GestaoConta")]
[Route("api/v1/dados-profissionais")]
public sealed class DadosProfissionaisController(IDadosProfissionaisService dadosService) : ControllerAutenticadoBase
{
    [HttpGet("situacao")]
    public async Task<IActionResult> Situacao(CancellationToken ct) => Ok(RespostaApi<SituacaoPerfilProfissionalResponse>.ComSucesso(await dadosService.ObterSituacaoAsync(ObterUsuarioId(), ObterContaPsicologoId(), ct)));

    [HttpPost("adiar")]
    public async Task<IActionResult> Adiar(CancellationToken ct)
    {
        await dadosService.AdiarAsync(ObterUsuarioId(), ObterContaPsicologoId(), ct);
        return NoContent();
    }

    /// <summary>Conta nova ainda sem perfil responde 200 com <c>dados: null</c>: falta preencher, não é erro.</summary>
    [HttpGet]
    public async Task<IActionResult> Obter(CancellationToken ct) =>
        Ok(RespostaApi<DadosProfissionaisResponse?>.ComSucesso(await dadosService.ObterAsync(ObterUsuarioId(), ObterContaPsicologoId(), ct)));

    [HttpPut]
    public async Task<IActionResult> Salvar(SalvarDadosProfissionaisRequest request, CancellationToken ct) => Ok(RespostaApi<DadosProfissionaisResponse>.ComSucesso(await dadosService.SalvarAsync(ObterUsuarioId(), ObterContaPsicologoId(), request, ObterIp(), ObterUserAgent(), ct)));
}
