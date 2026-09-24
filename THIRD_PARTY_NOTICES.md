# Third-party notices

The root MIT license applies to original MicWeave contributions, not to a
replacement of any upstream license. Preserve the following notices in source
and binary distributions.

| Component | License and notice |
| --- | --- |
| Virtual Cables-derived USB audio descriptors/device code | BSD-2-Clause; `src/MicWeave.VirtualUsb/LICENSE.Virtual-Cables.txt`; original attribution remains in the source |
| usbip-win2 0.9.8.0 signed transport, downloaded at build time | BSD-2-Clause; `src/MicWeave.VirtualUsb/LICENSE.usbip-win2.txt`; [upstream](https://github.com/vadimgrn/usbip-win2) |
| NAudio.Core / NAudio.Wasapi 2.2.1 | MIT; `src/GameMusicShare.App/Assets/LICENSE.NAudio.txt`; [upstream](https://github.com/naudio/NAudio) |
| Inter variable font | SIL Open Font License 1.1; `src/GameMusicShare.App/Assets/Fonts/OFL-Inter.txt`; [upstream](https://github.com/rsms/inter) |
| Go runtime and standard library linked into the helper | BSD-style; `licenses/LICENSE.Go.txt` and `licenses/PATENTS.Go.txt`; [upstream](https://go.dev/LICENSE) |
| Self-contained .NET runtime 8.0.31 | MIT plus third-party licenses; `licenses/LICENSE.DotNet.txt` and `licenses/THIRD-PARTY-NOTICES.DotNet.txt`; copied from the exact NuGet runtime pack |
| Windows Desktop runtime 8.0.31 | MIT plus upstream WPF notices; `licenses/LICENSE.WindowsDesktop.txt` and `licenses/THIRD-PARTY-NOTICES.WindowsDesktop.txt`; [WPF notices](https://github.com/dotnet/wpf/blob/v8.0.31/THIRD-PARTY-NOTICES.TXT) |
| Windows SDK .NET projection 10.0.22000.56 | Microsoft Windows SDK terms, `licenses/LICENSE.WindowsSDK.rtf`, from the package's [license link](https://aka.ms/WinSDKLicenseURL); not relicensed by MicWeave |
| C#/WinRT runtime | MIT; `licenses/LICENSE.CsWinRT.txt`; [upstream](https://github.com/microsoft/CsWinRT) |

The build copies these notices into the installation payload. When updating a
toolchain or runtime, refresh its notices from that exact distribution. NSIS is
a build tool; its generated installer stub licensing is described by
[NSIS](https://nsis.sourceforge.io/License).

The MicWeave icon is project artwork, not an Apple or Microsoft brand asset.
Other companies' names describe interoperability, not endorsement.
