namespace IrisXe.Thermal.Core.Access;

/// <summary>
/// Mecanismo usado na FASE 1 (sem driver): nenhuma leitura real e possivel.
/// Toda consulta retorna "Nao disponivel" com o motivo explicito — nunca um valor presumido
/// (requisito 12: "Se algum dado nao puder ser obtido, apresentar claramente: Nao disponivel
/// — motivo da falha").
/// </summary>
public sealed class UnavailableMsrReader : IMsrReader
{
    public const string DefaultReason =
        "Não disponível — nenhum mecanismo de leitura de MSR está instalado nesta fase. "
        + "O aplicativo Fase 1 não instala drivers nem altera configurações do Windows "
        + "(conforme autorização recebida). A leitura real será habilitada apenas na Fase 2, "
        + "com o driver somente-leitura revisado e autorizado.";

    public string MechanismName => "Indisponível (Fase 1 — sem driver)";

    public bool IsSimulated => false;

    public bool IsAvailable => false;

    public string StatusDescription => DefaultReason;

    public MsrReadResult Read(int processorGroup, int processorNumber, int msrAddress)
    {
        if (!Security.ReadPolicy.IsAllowed(msrAddress))
        {
            return MsrReadResult.Fail(
                $"Endereço 0x{msrAddress:X} não está na lista autorizada do projeto (0x1A2, 0x19C, 0x1B1). Leitura recusada.",
                87); // ERROR_INVALID_PARAMETER
        }

        return MsrReadResult.Fail(DefaultReason, 3); // ERROR_PATH_NOT_FOUND (mecanismo ausente)
    }
}
