---
name: signed-sideload-build
description: >-
  Produce a signed, sideloadable MSIX bundle of QuinSlate to install and test on
  another Windows machine (multi-arch x86/x64/arm64, self-signed, with a public cert
  and install instructions). Use ONLY when the user explicitly asks to run this skill
  by name (e.g. "run signed-sideload-build", "/signed-sideload-build", "use the signed
  sideload build skill"). Do NOT auto-trigger on generic requests like "build the app",
  "make a release", "package it", or "publish to the Store" — those are handled without
  this skill unless the user names it.
---

# Signed sideload build

Produce a signed `.msixbundle` of QuinSlate that installs cleanly on a **different**
Windows laptop, plus the public certificate and install notes the target machine needs.
QuinSlate is MSIX-packaged with a Store-associated publisher identity, so a plain
`dotnet build` does **not** yield something installable elsewhere — this workflow does.

The fast path is one script. The rest of this file explains the traps it encodes, so
you can diagnose when something deviates. **Run the whole thing from PowerShell.**

## Fast path

```powershell
# Marker is optional: a type/string a recent commit added, to prove that feature
# actually compiled in (belt-and-suspenders on top of the git-SHA check).
powershell -File .claude/skills/signed-sideload-build/scripts/build_signed_bundle.ps1 -Marker "EmojiSpriteAtlas"
```

That script: ensures the signing cert, **cleans**, builds+packages all three arches,
signs, **verifies the bits against HEAD**, and stages deliverables into `..\dist\`. If
verification fails it stops before staging, so a stale bundle never reaches the user.

Then read back the final section of its output and relay the deliverable paths.

## The five traps this workflow exists to prevent

Each of these actually bit us. They fail *silently* — the build goes green and you ship
old or broken bits — which is why verification (trap 4) is mandatory, not optional.

1. **Stale per-architecture binaries.** MSBuild reuses a per-arch publish output from an
   earlier session even when source changed. Symptom we hit: x64 was fresh, x86/arm64
   were three commits old; the bundle installed old code on the target laptop and looked
   like "the recent fixes aren't there." Fix: **delete `bin/` and `obj/` before building**
   so every arch recompiles from current source.

2. **XAML generator breaks on `/t:Restore,Build`.** Restoring and building in one target
   list evaluates the build before the restored WinUI targets load, so the XAML source
   generator never runs — you get a flood of `InitializeComponent`/generated-field
   `CS0103` errors and `CS5001`. Fix: use the **`/restore` switch** (separate phase), not
   `/t:Restore,Build`.

3. **A plain `Build` emits no bundle.** The default build compiles but doesn't package.
   Fix: `/p:GenerateAppxPackageOnBuild=true` together with `/p:AppxBundle=Always` and
   `/p:UapAppxPackageBuildMode=SideloadOnly`.

4. **A green build is not proof of current code.** See trap 1 — the only reliable check is
   to open the produced bundle and inspect the actual assemblies. `verify_bundle.ps1`
   extracts every arch's `QuinSlate.Ui.dll` and compares its embedded ProductVersion git
   SHA to HEAD. (When checking for a specific type/string marker, search the **ASCII**
   rendering of the DLL — .NET metadata names are UTF-8; searching UTF-16 gives false
   negatives on type names, which fooled us once.)

5. **MSBuild signing is flaky; the cert subject must match the manifest.** MSBuild's
   `PackageCertificatePassword` import fails with `APPX0105`/`APPX0107`, so we sign with
   **signtool** after the build. And the signing cert's subject must **exactly equal** the
   manifest `<Identity Publisher>` (a Store GUID, `CN=728BA5DB-...`) or the signature
   won't bind to the package identity and Windows rejects the install. The script reads
   the Publisher out of the manifest and creates/reuses a matching self-signed cert.

## Same-version reinstall caveat (tell the user)

If the rebuild keeps the **same version number**, Windows will **not** replace an existing
install of that version on the target machine — it silently keeps the old one, so the user
"reinstalls" and still sees old behavior. Two options, surface both:

- **Uninstall first** on the target: `Get-AppxPackage *QuinSlate* | Remove-AppxPackage`,
  then install the new bundle. (`INSTALL.md` documents this.)
- **Bump the version** so installs upgrade cleanly. Per `CLAUDE.md` the single source of
  truth is `<Version>` in `QuinSlate.Ui.csproj`; the manifest identity is derived from it
  on build. Edit that one value, rebuild, and future installs upgrade in place. Prefer this
  when the user will iterate more than once.

## Deliverables

Staged in `<repo-parent>\dist\` (outside the git repo, so it isn't accidentally committed):

- `QuinSlate_<version>_x86_x64_arm64.msixbundle` — signed app, runs on any CPU
- `QuinSlate-Signing.cer` — public cert; the target trusts this once
- `INSTALL.md` — trust-cert → (uninstall old) → install steps
- `_private-keys-do-not-share/QuinSlate-Signing.pfx` — private key; **never** share this

## If the user only wants one architecture

The default multi-arch bundle just works everywhere, so prefer it. Only if the user
explicitly wants a smaller single-arch package, narrow `AppxBundlePlatforms` (e.g.
`x64`) — but a Surface/Snapdragon target needs `arm64`, so don't guess; ask.

## Doing it by hand

If the script can't run (unusual environment, tool paths differ), the exact MSBuild
invocation and signtool call live inside `scripts/build_signed_bundle.ps1` with inline
comments — read it and run the steps manually. Always finish with `verify_bundle.ps1`
before handing anything to the user.
