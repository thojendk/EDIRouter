# EDIRouter

Console app that sorts EDIFACT files into `outputpath\<recipient>\<environment>` subdirectories
by parsing the UNB segment. See `prd.md` for the full requirements.

- Target: .NET 9, `win-x64`, self-contained, single-file, trimmed (see `EDIRouter/EDIRouter.csproj`).
- End-to-end smoke test: `test.bat`.

## Publishing

```powershell
dotnet publish EDIRouter\EDIRouter.csproj -c Release -o publish
```

**Signing is automatic.** `EDIRouter.csproj` imports `Sign\Sign.targets`, whose
`SignPublishedExe` target runs after `Publish` and invokes `Sign\SignExe.ps1` on the published
exe. If signing fails the publish fails — a publish either produces a signed exe or no usable
output at all.

- Works with or without `-o`; the target resolves the exe from `$(PublishDir)`.
- Plain `dotnet build` does not sign (the target hangs off `Publish` only).
- Opt out of signing for a one-off build: `dotnet publish -p:SignAfterPublish=false`.
- Override the shell with `-p:PowerShellExe=pwsh` (defaults to Windows PowerShell).

Verify manually at any time:

```powershell
Get-AuthenticodeSignature .\publish\EDIRouter.exe | Format-List Status, StatusMessage
```

Expected: `Status : Valid`.

## The code signing certificate

Self-signed, `CN=Thomas Jensen, O=Profital`, RSA 2048, 10 year validity, non-exportable key,
in `CurrentUser\My`. Signing uses SHA256 with the DigiCert timestamp server.

**Current thumbprint: `B66EBE0327FE11FA42AC4F95C121D952CE419E64`**

### This certificate is shared across solutions for the same customer

The same certificate is deliberately reused for other solutions built for this customer, so the
customer's antivirus and trusted-publisher whitelisting only ever has to cover one thumbprint.
Consequences:

- **`CreateSignature.ps1` is idempotent.** Re-running it reuses the existing certificate and only
  refreshes `Thumbprint.txt`, `EDIRouterCodeSigning.cer` and the `$Thumbprint` line in
  `SignExe.ps1`. It mints a new certificate only when none exists, or with `-Force`.
- **Do not pass `-Force` casually.** A new certificate means a new thumbprint, which the customer
  must whitelist all over again, and previously shipped binaries chain to the old one.
- **To reuse in another solution, copy the whole `Sign` folder into it and add one line to that
  project's csproj**, with the path relative to the csproj:

  ```xml
  <Import Project="..\Sign\Sign.targets" />
  ```

  Nothing else needs adjusting: `Sign.targets` locates `SignExe.ps1` from its own directory, so
  any folder layout works, and it derives the exe name from `$(AssemblyName)`. The certificate is
  not copied — it stays in the certificate store, and the thumbprint baked into `SignExe.ps1`
  resolves it. Trust is already established for this user, so a new project signs with no setup.

**Machine constraint:** the private key is created `NonExportable`, so it cannot be moved to
another machine or user profile. Reuse works on this machine under this user account only. A
build server, a reinstall, or a second developer cannot sign with this certificate — they would
need their own, with a new thumbprint for the customer to trust. If signing must ever move off
this machine, the certificate has to be recreated with `-KeyExportPolicy Exportable` from the
start; that decision cannot be made retroactively.

### Local trust

Because the certificate is self-signed, its public key (`Sign\EDIRouterCodeSigning.cer`) is also
imported into `CurrentUser\Root` (trusted root CA) and `CurrentUser\TrustedPublisher`. Without the
Root entry the signature validates as *"a certificate chain ended in a root certificate which is
not trusted"* and the publish fails. This is needed once per machine/user:

```powershell
certutil -user -addstore -f Root .\Sign\EDIRouterCodeSigning.cer
Import-Certificate -FilePath .\Sign\EDIRouterCodeSigning.cer -CertStoreLocation Cert:\CurrentUser\TrustedPublisher
```

Note: `Import-Certificate` into `Cert:\CurrentUser\Root` raises a Windows confirmation dialog and
fails with *"UI is not allowed in this operation"* in a non-interactive shell — use
`certutil -user -addstore -f Root` for the root store instead.

### Gotcha: PowerShell module path

When MSBuild invokes the signing script it goes through a `pwsh -> cmd.exe -> powershell.exe`
chain, and the inherited `PSModulePath` hides the child host's own modules. That makes
`Microsoft.PowerShell.Security` fail to autoload, so the `Cert:\` drive silently finds nothing and
`Set-AuthenticodeSignature` does not exist. Two guards are in place: the `Exec` task in
`Sign.targets` clears `PSModulePath`, and `SignExe.ps1` resets it if the cmdlet is missing and
looks the certificate up through the .NET `X509Store` API rather than the `Cert:\` provider.
Keep both if you touch this.
