# Third-Party Notices

The source code of this project is licensed under [MIT-0](LICENSE) and can be used without attribution.

The **compiled application** additionally contains the following open-source libraries. Their licenses
(MIT) require that the copyright notice and license text are included when the binaries are redistributed.
This file is copied next to `UploadManager.exe` on build/publish, so shipping the publish folder as-is
fulfils that requirement.

| Component | License | Copyright |
|---|---|---|
| [WPF UI](https://github.com/lepoco/wpfui) (`WPF-UI`, `WPF-UI.Abstractions`) | MIT | © 2021–2026 Leszek Pomianowski and WPF UI Contributors |
| ↳ [Fluent UI System Icons](https://github.com/microsoft/fluentui-system-icons) (bundled in WPF UI) | MIT | © 2020 Microsoft Corporation |
| ↳ [VirtualizingWrapPanel](https://github.com/sbaeumlisberger/VirtualizingWrapPanel) (bundled in WPF UI) | MIT | © 2019 S. Bäumlisberger |
| [.NET Community Toolkit](https://github.com/CommunityToolkit/dotnet) (`CommunityToolkit.Mvvm`) | MIT | © .NET Foundation and Contributors |
| [Microsoft.Extensions.*](https://github.com/dotnet/runtime) (Hosting, Http, Options, Logging, …) | MIT | © .NET Foundation and Contributors |
| [.NET Runtime / WPF](https://github.com/dotnet/wpf) (only in self-contained builds) | MIT | © .NET Foundation and Contributors |

Development-only dependencies (not part of the application): xUnit.net (Apache-2.0), Microsoft.NET.Test.Sdk (MIT).

## MIT License (applies to all components listed above)

```
Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
