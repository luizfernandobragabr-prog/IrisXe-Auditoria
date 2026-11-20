# RELATÓRIO DE VALIDAÇÃO — Fase 1

Ambiente de compilação: .NET SDK 10.0.404 (Linux arm64, cross-compile para win-x64).

## 1. Compilação

| Item | Resultado |
|---|---|
| `dotnet build -c Release` (3 projetos) | **Sucesso — 0 erros, 0 avisos** |
| `dotnet publish -r win-x64 --self-contained -p:PublishSingleFile=true` | **Sucesso** — `IrisXeThermalReader.exe` |
| Cabeçalho do executável | `MZ` / `PE\0\0` · Machine `0x8664` (AMD64) · Optional header `0x20B` (PE32+) · Subsystem 2 (Windows GUI) |
| SHA-256 do .exe | `4371266b5b03f6c7f2361d6d7f11487ead3482642cd8c727baf777c9f6750892` |

**Limitação declarada:** o executável NÃO pôde ser executado neste ambiente (Linux). A interface
WinForms e a identificação de hardware (registro do Windows / WMI) precisam ser validadas por
você no próprio equipamento. Nenhuma alegação de funcionamento da UI no Windows é feita aqui.

## 2. Testes unitários — `dotnet test` (xUnit)

**27 aprovados / 0 falhas / 0 ignorados.**

| Grupo | Testes | Cobertura |
|---|---|---|
| TemperatureTargetDecodeTests | 5 | TjMAX não presumido (100 e 105 °C), offset 13 → alvo 87 °C, divergência [29:24] × [27:24], reservados não interpretados, Tau sem conversão |
| ThermStatusDecodeTests | 4 | Temperatura = TjMAX − readout, leitura inválida → nula, throttling corrente + log, PROCHOT externo |
| PackageThermStatusDecodeTests | 3 | Logs do pacote, bits ≥ 23 não documentados, origem não confirmável só com logs |
| CpuSignatureTests | 3 | i7-1195G7 (F6/M140) aceito; Ice Lake (M126) e AMD recusados |
| ReadPolicyTests | 6 | Somente 0x1A2/0x19C/0x1B1; recusa 0xCE/0x1FC/0x610; "Não disponível — motivo" sem valores |
| TransitionAndExportTests | 6 | Transições com nota temporal; auditoria com todos os campos; JSON declara 0 escritas; comparação ThrottleStop |

Correção registrada durante a validação: um vetor de teste do pacote (`0x88280800`) estava errado
— setava apenas o bit 11, não o bit 3. O decodificador estava correto; o vetor foi corrigido para
`0x88280808`. Erro do autor do teste, documentado aqui por transparência.

## 3. Paridade C# × Python (decodificador do hub)

`backend/tests/test_msr_decode.py` executa os mesmos vetores contra o decodificador Python do
hub — mesmas máscaras e resultados (inclusive máscaras de 64 bits idênticas).

## 4. Auditoria estática de escrita

Busca no código executável (excluindo comentários): `wrmsr`, `__writemsr`, `WriteMsr`,
`DeviceIoControl`, `CreateFile`, `outp`, `MmMapIoSpace`, `DllImport`, `LibraryImport`, `extern`,
`Process.Start`, `Registry.SetValue`, `OpenSubKey(..., true)` → **0 ocorrências**.
O contrato `IMsrReader` declara apenas `Read()` e propriedades somente-leitura.

## 5. O que NÃO foi validado (depende de acesso real ao MSR — Fase 2)

- Leitura real dos registradores no i7-1195G7.
- Se o TCC Activation Offset real é 13 (valor do ThrottleStop).
- Comparação antes/depois do ThrottleStop com valores reais.
- Consistência de 0x1A2/0x1B1 entre os 8 processadores lógicos em hardware real.
