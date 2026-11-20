namespace IrisXe.Thermal.Core.Decoding;

/// <summary>
/// Analise do PROCHOT combinando IA32_THERM_STATUS (por nucleo) e PACKAGE_THERM_STATUS (pacote).
///
/// Fundamentacao (datasheet TGL 631121-012 §4.1.3.3 "PROCHOT# Signal"):
///   "The PROCHOT# (processor hot) signal is asserted by the processor when the TCC is active...
///    The PROCHOT# signal can be configured to the following modes: Input Only / Output Only /
///    Bi-Directional" e, no modo entrada (padrao): "the processor will only monitor PROCHOT#
///    assertions and respond by setting the maximum frequency to 10KHz" — ou seja, o sinal PODE
///    vir de componentes externos (VR, fonte, etc.).
///
/// Regra de classificacao adotada (sem especulacao):
///   1. Status termico interno corrente (bit 0) em nucleo ou pacote  -> "termico interno corrente".
///   2. Sem bit 0, mas com PROCHOT Event (bit 2) corrente            -> "assercao externa corrente"
///      (o SDM define o bit 2 como sinal asserido por outro componente).
///   3. Apenas bits de LOG setados (bit 1/3)                          -> "origem NAO confirmavel
///      por leitura de MSR" — o aplicativo declara a limitacao em vez de atribuir causa.
///   4. Nenhum indicador                                             -> "nenhum evento detectado".
/// </summary>
public sealed record ProchotAssessment
{
    public required string Classification { get; init; }

    public required string Detail { get; init; }

    public required string Caveat { get; init; }

    public static ProchotAssessment Assess(ThermStatusDecoding core, PackageThermStatusDecoding package)
    {
        var internalCurrent = core.ThermalStatus || package.ThermalStatus;
        var externalCurrent = core.ProchotEvent || package.ProchotEvent;
        var anyLog = core.ThermalStatusLog || core.ProchotLog || package.ThermalStatusLog || package.ProchotLog;

        const string caveat =
            "ATENCAO: conforme o datasheet TGL 631121-012 §4.1.3.3, o sinal PROCHOT# pode operar como entrada, saida ou "
            + "bidirecional (entrada e o padrao documentado). A leitura destes MSRs NAO identifica qual dispositivo fisico "
            + "asseriu a linha; quando apenas o registro historico esta setado, a origem (evento termico interno x solicitacao "
            + "externa) NAO pode ser confirmada por esta leitura.";

        string classification;
        string detail;

        if (internalCurrent && externalCurrent)
        {
            classification = "Evento termico interno corrente COM sinal externo simultaneo";
            detail = "Bit 0 (Thermal Status) e bit 2 (PROCHOT#/FORCEPR# Event) ativos em nucleo e/ou pacote na mesma amostragem: "
                   + "ha atuacao do controle termico interno e assercao corrente de sinal por componente externo. A precedencia "
                   + "temporal entre os dois nao pode ser determinada dentro do intervalo de amostragem.";
        }
        else if (internalCurrent)
        {
            classification = "Evento termico interno corrente (atuacao do controle termico)";
            detail = "Bit 0 (Thermal Status) ativo em nucleo e/ou pacote: a temperatura atingiu a temperatura de ativacao do TCC "
                   + "(TjMAX − TCC Activation Offset). O PROCHOT# e asserido pelo proprio processador quando o TCC esta ativo.";
        }
        else if (externalCurrent)
        {
            classification = "PROCHOT corrente asserido por componente externo";
            detail = "Bit 2 (PROCHOT#/FORCEPR# Event) ativo sem status termico interno (bit 0) em nucleo ou pacote: conforme o SDM, "
                   + "o bit 2 indica sinal asserido por outro componente da plataforma — indicacao forte de solicitacao EXTERNA, "
                   + "nao de aquecimento interno.";
        }
        else if (anyLog)
        {
            classification = "Apenas registro historico — origem nao confirmavel por esta leitura";
            detail = "Existem bits de LOG setados (eventos anteriores), mas nenhum indicador corrente. Os registros nao distinguem "
                   + "se a assercao original partiu do sensor termico interno ou de um componente externo; qualquer afirmacao de "
                   + "causa seria especulacao. O log em software deste aplicativo registra as transicoes observadas entre amostras.";
        }
        else
        {
            classification = "Nenhum evento PROCHOT/termico detectado nesta amostragem";
            detail = "Bits 0 a 3 inativos em IA32_THERM_STATUS e PACKAGE_THERM_STATUS na ultima amostragem.";
        }

        return new ProchotAssessment { Classification = classification, Detail = detail, Caveat = caveat };
    }
}
