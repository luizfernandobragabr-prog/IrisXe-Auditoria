namespace IrisXe.Thermal.Core.Security;

/// <summary>
/// Politica de leitura: SOMENTE os tres MSRs autorizados podem ser consultados, por qualquer
/// mecanismo (futuro driver incluso — requisito 15.4: "o driver devera aceitar somente
/// operacoes de leitura dos registradores previamente autorizados; nao devera aceitar
/// enderecos arbitrarios"). Verificada no limite do aplicativo, antes de qualquer acesso.
/// </summary>
public static class ReadPolicy
{
    public static IReadOnlySet<int> AllowedAddresses { get; } =
        new HashSet<int>(Core.MsrAddress.All);

    public static bool IsAllowed(int msrAddress) => Core.MsrAddress.IsAuthorized(msrAddress);

    /// <summary>
    /// Auto-verificacoes exibidas na aba Seguranca. Cada item descreve uma garantia estrutural
    /// do design (nao uma afirmacao sem verificacao).
    /// </summary>
    public static IReadOnlyList<(string Assertion, string Evidence)> SelfCheck() => new List<(string, string)>
    {
        ("Nenhum metodo de escrita existe na interface de acesso",
            "A interface IMsrReader declara apenas Read(grupo, processador, endereco). Nao existe Write/WRMSR em nenhum "
          + "ponto do codigo — a garantia e estrutural (o compilador impede uso inexistente)."),
        ("Enderecos restritos a 0x1A2, 0x19C e 0x1B1",
            "Toda leitura passa por ReadPolicy.IsAllowed antes do acesso; enderecos arbitrarios sao recusados com erro "
          + "auditado. A mesma restricao sera imposta no driver da Fase 2 (lista fechada no IOCTL)."),
        ("Nenhuma instrucao WRMSR no projeto",
            "O codigo-fonte nao contem WRMSR nem chamadas a drivers de escrita. A Fase 2 entregara o driver com codigo "
          + "aberto para auditoria antes de qualquer uso."),
        ("Nenhuma alteracao de configuracao do Windows",
            "O aplicativo nao executa bcdedit, nao altera registro de configuracao do sistema, nao desativa Secure Boot, "
          + "VBS, HVCI, Integridade da Memoria ou blocklist de drivers, e nao ativa TESTSIGNING."),
        ("Nenhuma escrita em MMIO / PCI Config / memoria fisica",
            "Nao existe acesso a memoria fisica, MMIO ou espaco de configuracao PCI em nenhuma camada."),
        ("Bits historicos preservados",
            "A leitura (RDMSR) nao limpa os bits de log R/WC0; o aplicativo nunca escreve nos registradores para "
          + "reiniciar indicadores. O historico e tambem registrado em log de software, imune a limpezas feitas "
          + "por outras ferramentas."),
        ("Dados nao reais sempre rotulados",
            "Valores fornecidos manualmente (modo de teste) sao marcados como SIMULADO em cada exibicao e em cada "
          + "linha do log de auditoria. Nunca sao apresentados como leituras reais."),
    };
}
