# DEBLOAT

Windows 11 limpo, do jeito certo. Um programa que monta uma instalação do Windows 11 sem o lixo
de fábrica, com um preset pensado item por item, e já instala os seus apps, os pré-requisitos de
jogos e o DNS que você escolher.

> Em desenvolvimento. Hoje o programa gera o `autounattend.xml` (coloque na raiz de um pendrive
> de instalação do Windows 11). Download da ISO e gravação do pendrive vêm a seguir: veja o
> [PLAN.md](PLAN.md).

## Compilar

```
git clone --recurse-submodules https://github.com/bernardomcr/DEBLOAT
dotnet test Debloat.slnx
dotnet run --project src/Debloat.App
```

Requer o .NET 10 SDK.

## Créditos

- Gerador de `autounattend.xml`: [cschneegans/unattend-generator](https://github.com/cschneegans/unattend-generator) (MIT).
- Configuração de referência: canal 1155 do ET.
- Ideias: Win11Debloat, Sophia Script, WinUtil, O&O ShutUp10++, Talon, Tiny11, Ninite, Ludusavi.
