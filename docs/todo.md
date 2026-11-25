# To do

Open items left after the 0.2.0-preview release (2026-11-25).

- [ ] Ship a README in both NuGet packages (`PackageReadmeFile`). nuget.org warns that it is
      missing from `Orikago.Sdk` and `Orikago.Templates`.
- [ ] Replace `Orikago.Templates::<version>` with `Orikago.Templates@<version>`; the dotnet CLI
      reports `::` as deprecated. Used in `README.md`, `README.zh-TW.md`,
      `docs/marketplace-overview.md` and `docs/releasing.md`.
- [ ] Create the `v0.2.0` GitHub Release and rebuild the VSIX, which is still version 0.1.0.
- [ ] Verify the published packages on Linux and macOS. The release was only checked on Windows.
