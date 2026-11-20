# IRIS XE THERMAL MSR READER

Aplicativo Windows (C# / Windows Forms / .NET 10 LTS) para **diagnóstico e leitura segura** dos
registradores térmicos do Intel Core i7-1195G7 (Tiger Lake-U) do notebook Positivo Master 4250.

**Fase 1 — sem driver.** Este aplicativo não instala drivers, não altera nenhuma configuração do
Windows e não executa nenhuma operação de escrita (não existe WRMSR, nem escrita em MMIO, PCI
Config ou memória física em nenhuma camada). A leitura real dos MSRs depende do driver
somente-leitura da **Fase 2**, que só será implementada após revisão técnica e autorização
explícita (conforme combinado).

## Escopo por fase (honesto)

| Funcionalidade | Estado |
|---|---|
| Identificação do processador (família/modelo/stepping via registro derivado de CPUID) | **Implementado** |
| Trava de hardware (recusa leituras fora do i7-1195G7 / TGL 0x8C) | **Implementado** |
| Decodificação documentada do MSR 0x1A2 (TjMAX [23:16], offset [29:24] + conferência [27:24], Tau [6:0] em bruto) | **Implementado** |
| Decodificação documentada do MSR 0x19C (corrente × histórico, PROCHOT, readout) | **Implementado** |
| Decodificação documentada do MSR 0x1B1 (pacote) | **Implementado** |
| Temperatura-alvo média (TjMAX − offset) | **Implementado** |
| Motor de diagnóstico: amostragem periódica (5 s padrão), transições com horário da detecção | **Implementado** |
| Auditoria por leitura (LP, MSR, valor bruto, data/hora, resultado, erro) — log sem limpeza | **Implementado** |
| Comparação antes/depois ThrottleStop (linha de base rotulada + aviso de causalidade) | **Implementado** |
| Exportação CSV / JSON / NDJSON | **Implementado** |
| Aba de teste manual — valores fornecidos pelo usuário, **sempre rotulados como SIMULADOS** | **Implementado** |
| **Leitura REAL dos MSRs** | **Pendente — depende do driver somente-leitura (Fase 2, sujeito a autorização)** |
| Garantia de lista fechada de endereços no driver (sem MSR arbitrário) | **Pendente — Fase 2** |

## Estrutura

```
desktop/
  IrisXeThermal.sln
  src/
    IrisXe.Thermal.Core/          Núcleo puro (sem acesso a hardware):
      MsrAddress.cs               Lista fechada de MSRs autorizados
      Decoding/                   Decodificadores 0x1A2 / 0x19C / 0x1B1 + avaliação PROCHOT
      Hardware/CpuSignature.cs    Assinatura CPUID (família/modelo/stepping) + trava de alvo
      Access/                     IMsrReader (contrato SEM escrita) + leitores da Fase 1
      Diagnostics/                Motor, auditoria, transições, comparação ThrottleStop
      Export/                     Exportadores CSV/JSON/NDJSON
      Security/ReadPolicy.cs      Política de leitura + auto-verificações
    IrisXe.Thermal.Core.Tests/    Testes unitários (xUnit) da decodificação e das políticas
    IrisXe.Thermal.App/           Interface Windows Forms (PT-BR), 8 abas
  docs/
    RELATORIO_SEGURANCA.md        Relatório de segurança
    VALIDACAO.md                  Resultados dos testes de compilação e decodificação
```

## Como compilar (no Windows 11 x64)

1. Instale o **.NET 10 SDK (LTS)**: <https://dotnet.microsoft.com/download/dotnet/10.0>
2. Em um terminal na pasta `desktop/`:

```bat
dotnet build -c Release
dotnet run --project src\IrisXe.Thermal.App -c Release
```

3. Executável autocontido x64 (não requer runtime instalado na máquina alvo):

```bat
dotnet publish src\IrisXe.Thermal.App -c Release -r win-x64 --self-contained true ^
  /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true
```

O binário sai em `src\IrisXe.Thermal.App\bin\Release\net10.0-windows\win-x64\publish\IrisXeThermalReader.exe`.
Como não é assinado com certificado, o SmartScreen pode exibir aviso — clique em "Mais informações →
Executar assim mesmo", ou use `dotnet run`. O manifesto exige execução como Administrador
(necessário para o mecanismo de leitura da Fase 2; nenhuma configuração do sistema é alterada).

## Como testar

```bash
dotnet test desktop/src/IrisXe.Thermal.Core.Tests
```

Os vetores de teste são **exemplos determinísticos** construídos a partir das definições
documentadas — não são leituras de hardware. Cada decodificador cita a fonte Intel de cada campo.

## Mecanismo de leitura (estado atual e plano)

- **Fase 1 (este código):** `UnavailableMsrReader` — toda leitura retorna
  **"Não disponível — motivo"**; e `ManualMsrReader` — valores fornecidos manualmente,
  **rotulados como SIMULADOS em todas as telas e no log**. Nenhum valor presumido é exibido.
- **Fase 2 (sujeita a autorização):** driver de kernel próprio **somente-leitura** com um único
  IOCTL (leitura) e **lista fechada de endereços** (0x1A2, 0x19C, 0x1B1 — nenhum MSR arbitrário é
  aceito), assinado por **atestação Microsoft** (Secure Boot, VBS, HVCI e blocklist permanecem
  ativos — sem TESTSIGNING). O código-fonte do driver será entregue para auditoria antes de
  qualquer compilação/instalação, com relatório de riscos. A interface de comunicação do
  aplicativo (`IMsrReader`) não possui operações de escrita por construção.
- **Alternativas descartadas** (análise completa no hub web, página "Viabilidade"): WinRing0
  (bloqueado pela blocklist da Microsoft e sem garantia de somente-leitura), driver do Intel PCM
  (exige TESTSIGNING para carregar no Windows 11 atual), RWEverything/WinIo (fechados/abandonados),
  depuração de kernel (altera configuração de boot).

## Referências documentais

- Intel 11th Generation Core Processor Families Datasheet, Vol. 1, documento 631121-012
  (§4.1.3.1 Adaptive Thermal Monitor; §4.1.3.1.1 TCC Activation Offset + Tau; §4.1.3.2 DTS;
  §4.1.3.3 PROCHOT# Signal; Tabela 4-4 Junction Temperature Specifications).
- Intel 64 and IA-32 Architectures Software Developer's Manual, Vol. 4 (tabelas de
  MSR_TEMPERATURE_TARGET, IA32_THERM_STATUS e MSR_PACKAGE_THERM_STATUS).
- Driver termico Linux `intel_tcc` (máscara 0x3F para o offset em TGL+) — corroboração cruzada.
