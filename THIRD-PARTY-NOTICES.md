# Third-party software

PortBridge uses the following open-source components. Their license texts are distributed in the `licenses/` directory of release packages.

- SSH.NET — MIT — https://github.com/sshnet/SSH.NET
- BouncyCastle.Cryptography — MIT — https://github.com/bcgit/bc-csharp
- Microsoft.Extensions.Logging.Abstractions and Microsoft.Extensions.DependencyInjection.Abstractions (if included by the resolved dependency graph) — MIT — https://github.com/dotnet/runtime
- Microsoft .NET and Windows Desktop runtime — MIT and third-party licenses — https://github.com/dotnet/runtime and https://github.com/dotnet/windowsdesktop

Exact versions are recorded by NuGet in `obj/project.assets.json` during build. The build script copies available package license texts and the runtime license notices into the release folder.
