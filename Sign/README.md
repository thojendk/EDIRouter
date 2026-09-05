# Code signing in another project

Signs the published exe automatically, with the certificate this customer's antivirus
already trusts. Two steps.

**1. Copy this whole `Sign` folder into the other repo.**

**2. Add one line to the project's `.csproj`**, with the path relative to that csproj:

```xml
<Import Project="..\Sign\Sign.targets" />
```

That's it. `dotnet publish` now signs the exe and fails the publish if signing fails.
Nothing else needs adjusting — the targets file finds the script next to itself and takes
the exe name from `$(AssemblyName)`, so any folder layout and any project name works.

## Notes

- **The certificate is not copied.** It lives in the Windows certificate store, and the
  thumbprint baked into `SignExe.ps1` finds it. Do not run `CreateSignature.ps1` — the
  certificate already exists, and a second one would mean a new thumbprint for the
  customer to whitelist. (It refuses to do that anyway unless you pass `-Force`.)
- **Same machine and user account only.** The private key is non-exportable, so it cannot
  move to a build server or another developer.
- Opt out for one build: `dotnet publish -p:SignAfterPublish=false`. Plain `dotnet build`
  never signs.
- Verify: `Get-AuthenticodeSignature .\publish\MyApp.exe` → `Status : Valid`.

On a machine where the certificate has never been trusted, the signature validates as an
untrusted root until its public key is imported once:

```powershell
certutil -user -addstore -f Root .\Sign\EDIRouterCodeSigning.cer
Import-Certificate -FilePath .\Sign\EDIRouterCodeSigning.cer -CertStoreLocation Cert:\CurrentUser\TrustedPublisher
```

Full background: `CLAUDE.md` in the EDIRouter repo.
