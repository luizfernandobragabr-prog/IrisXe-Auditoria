# RELATÓRIO DE SEGURANÇA — IRIS XE THERMAL MSR READER (Fase 1)

Data: (preenchida na entrega) · Versão: 1.0.0 · Fase: 1 (sem driver)

## 1. Princípio

O aplicativo é uma ferramenta de **diagnóstico exclusivamente de leitura**. A garantia central é
**estrutural**, não apenas de intenção:

1. A interface de acesso ao hardware (`IMsrReader`, em `IrisXe.Thermal.Core/Access/`) declara
   **apenas** `Read(processorGroup, processorNumber, msrAddress)`. Não existe método de escrita —
   o compilador impede que qualquer código do projeto invoque uma operação que não existe.
2. **Lista fechada de endereços**: toda leitura passa por `ReadPolicy.IsAllowed` antes do acesso.
   Endereços arbitrários (0xCE, 0x610, etc.) são recusados sem tocar em nada — inclusive no modo
   de valores manuais. O driver da Fase 2 (após autorização) repetirá esta lista no próprio IOCTL.
3. **Zero escrita**: o código não contém `WRMSR`, não escreve em MMIO, não escreve no espaço de
   configuração PCI, não acessa memória física e não altera registradores de controle (PL1/PL2,
   PP0/PP1, PROCHOT, TCC Activation Offset, tensões).
4. **Zero alteração no Windows**: o aplicativo não executa `bcdedit`, não desativa Secure Boot,
   VBS, HVCI, Integridade da Memória ou a blocklist de drivers vulneráveis, não ativa TESTSIGNING
   e não modifica o registro além de **ler** chaves públicas de descrição de hardware
   (`HKLM\HARDWARE\DESCRIPTION\...`, `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion`).
5. **Bits históricos preservados**: os bits de log (R/WC0) dos MSRs térmicos só são limpos por
   escrita de 0 feita por software. O aplicativo nunca escreve — portanto nunca limpa os registros
   históricos de hardware. Toda amostra também é registrada em log de software (imune a limpezas
   feitas por outras ferramentas).
6. **Dados não reais sempre rotulados**: o modo de valores manuais se identifica como
   "SIMULADO — não é leitura de hardware" em todas as telas e marca cada linha do log de
   auditoria. Nenhum valor simulado é apresentado como leitura real.
7. **Sem dependências de terceiros no caminho de acesso**: a Fase 1 não usa WinRing0, RWEverything,
   WinIo, RTCore64 nem driver do Intel PCM — os dois primeiros são bloqueados pela blocklist da
   Microsoft e todos expõem IOCTLs de escrita (análise completa no hub web, página Viabilidade).

## 2. Superfície de ataque considerada

| Vetor | Mitigação |
|---|---|
| Driver vulnerável (BYOVD) | Nenhum driver é instalado ou carregado na Fase 1. Na Fase 2, driver próprio, código aberto, lista fechada no IOCTL, assinatura de atestação |
| Abuso do driver por terceiros (leitura de MSRs por malware) | Fase 2: device object com ACL restrita a Administradores (SDDL) + hash SHA-256 fixado no aplicativo |
| Escrita acidental | Impossível por construção: não há caminho de escrita no código |
| Dados falsos enganando o operador | Rotulagem obrigatória de modo simulado em toda a UI e no log |
| Execução em hardware não alvo | Trava de assinatura CPUID (família 6, modelo 0x8C) com recusa explícita |

## 3. Riscos residuais declarados

- O executável distribuído sem assinatura comercial dispara aviso do SmartScreen.
- A leitura real (Fase 2) exige execução como Administrador (necessário ao carregar o driver);
  o aplicativo não faz nada de privileged além da leitura autorizada.
- Na Fase 1, os campos dependentes de MSR exibem "Não disponível — motivo"; nada é presumido.

## 4. Auto-verificações em execução

A aba "Segurança" do aplicativo executa `ReadPolicy.SelfCheck()` e exibe, em tempo de execução,
as afirmações acima com a evidência correspondente no código.
