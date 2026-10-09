# Notas para agentes

- Produto: instalador de Windows 11 debloated (janela WPF). Decisões de produto em `PLAN.md`; atualize-o quando uma decisão mudar. O preset padrão é o produto: toda mudança nele precisa de motivo (ganho × o que quebra).
- Decisões fixas do usuário: chave genérica do Pro e conta sem senha. Nada de ativadores, nem de tweaks placebo/perigosos (lista em `PLAN.md`).
- **Drivers**: nunca remover/omitir drivers de rede, armazenamento ou chipset; nunca tocar em BIOS/firmware nem em discos que não sejam o escolhido.
- .NET 10. Neste PC o SDK está em `%LOCALAPPDATA%\Microsoft\dotnet` (defina `DOTNET_ROOT` para ele). `dotnet test Debloat.slnx` roda tudo.
- Gerador do schneegans é submódulo em `external/unattend-generator` (clone com `--recurse-submodules`). Não edite ali; configure pelo `Configuration` em `Debloat.Core/Presets/UnattendBuilder.cs`. IDs de bloatware = `Remove` + `Token` (ou DisplayName sem espaços) do `resource/Bloatware.json` dele.
- Scripts que vão para dentro do XML ficam em `src/Debloat.Core/Scripts` (recurso embutido). `@@APPS@@`/`@@DNS@@` são trocados na geração. Depois de editar um .ps1, confira a sintaxe com `[System.Management.Automation.Language.Parser]::ParseFile`.
- Catálogo em `src/Debloat.Core/Data/apps.json`: confira todo ID novo com `winget show --id X --exact` (ou `-s msstore`) antes de adicionar.
- Textos visíveis em português. Mensagens de commit em português.
- Ver a janela sem mexer no mouse: abrir o .exe e capturar cada aba com UI Automation (`SelectionItemPattern.Select()` nos `TabItem`) + `PrintWindow` (flag 2).
