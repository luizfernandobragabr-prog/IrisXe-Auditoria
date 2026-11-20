using IrisXe.Thermal.Core.Diagnostics;

namespace IrisXe.Thermal.App;

/// <summary>
/// Textos fixos da interface (PT-BR). Centralizados para revisao tecnica facil.
/// </summary>
internal static class UiTexts
{
    public const string AppTitle = "IRIS XE THERMAL MSR READER — Diagnóstico térmico (somente leitura)";

    public const string ReadOnlyBanner =
        "MODO SOMENTE LEITURA — este aplicativo não executa WRMSR, não escreve em registradores, "
        + "não altera PL1/PL2, PROCHOT, TCC Offset, tensões ou qualquer configuração de hardware ou do Windows.";

    public const string Phase1Banner =
        "FASE 1 (sem driver): não há leitura real de MSR instalada. Campos dependentes de MSR exibem "
        + "\"Não disponível — motivo\". Use a aba \"Teste manual\" para exercitar a decodificação com valores "
        + "fornecidos manualmente — eles são SEMPRE rotulados como SIMULADOS e nunca são apresentados como leituras.";

    public const string SimulatedBanner =
        "MODO SIMULADO — os valores exibidos foram fornecidos manualmente e NÃO são leituras do hardware. "
        + "Toda linha do log de auditoria também está marcada como simulada.";

    public const string TauLimitation =
        "Limitação declarada: a conversão do valor bruto de Tau para segundos não é documentada para o mecanismo "
        + "de TCC do Tiger Lake. O valor é exibido em bruto. Não confundir com o Tau do Turbo Boost.";

    public const string OffsetNote =
        "Duas máscaras existem para o TCC Activation Offset: [29:24] conforme o datasheet Tiger Lake (631121-012, "
        + "§4.1.3.1.1) — fonte usada — e [27:24] conforme o SDM Vol. 4 genérico, exibido para conferência. "
        + "Qualquer divergência entre as duas é sinalizada.";

    public const string ProchotNote =
        "O sinal PROCHOT# pode operar como entrada, saída ou bidirecional (datasheet TGL §4.1.3.3; entrada é o "
        + "padrão documentado). Quando apenas os bits de LOG estão setados, a origem (evento térmico interno x "
        + "solicitação externa) NÃO pode ser confirmada por esta leitura — o aplicativo declara a limitação em "
        + "vez de atribuir causa.";

    public const string ComparisonDisclaimer = ComparisonReport.Disclaimer;

    public const string HistoricalBitsNote =
        "Bits de LOG (históricos) são R/WC0: apenas uma ESCRITA de 0 por software os limpa. A leitura (RDMSR) não "
        + "os limpa e este aplicativo nunca escreve nos registradores. Nota: outras ferramentas (inclusive o próprio "
        + "ThrottleStop, dependendo da operação) podem ter limpo esses bits antes — por isso todo evento também é "
        + "registrado no log de software, com horário da amostragem em que foi observado.";

    public const string SamplingHonestyNote =
        "Se um indicador permanece ativo entre duas leituras, a interface mostra que ele está ativo na última "
        + "amostragem. O início real de um evento não pode ser determinado com precisão dentro do intervalo de "
        + "amostragem — os horários exibidos são os da detecção.";

    public const string CpuIdNote =
        "A assinatura (família/modelo/stepping) é lida da cadeia 'Identifier' do registro do Windows "
        + "(HKLM\\HARDWARE\\DESCRIPTION\\System\\CentralProcessor), derivada pelo próprio sistema operacional da "
        + "instrução CPUID no boot — equivale a CPUID.1:EAX sem executar código nativo de usuário. O nome comercial "
        + "vem de 'ProcessorNameString'.";
}
