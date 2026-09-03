# Security and privacy

## Local data

- The unified-authentication password is stored through Windows Credential Manager.
- The password is passed only to the local Python child process through an environment variable and is never written to repository files, JSON settings, INI configuration, or logs.
- Captcha recognition is performed locally with `ddddocr`; captcha images are not uploaded to third-party recognition services.
- Runtime settings, logs, caches, and swap history are stored under `%LOCALAPPDATA%\AutoElectiveOrb` and are excluded from Git.
- Request and response bodies are not dumped unless a developer explicitly sets `AUTOELECTIVE_ALLOW_SENSITIVE_DUMPS=1`.

Before reporting a bug, remove student IDs, cookies, tokens, course selections, and other personal information from screenshots and logs.

## Reporting a vulnerability

Please open a GitHub security advisory instead of publishing credentials or an exploitable security issue in a public issue.

# Release signing

Tagged releases support Windows Authenticode signing when a trusted certificate is configured. Unsigned releases are explicitly marked inside the package and may trigger an unknown-publisher warning. Signing keys and certificate passwords must be stored only as encrypted GitHub Actions secrets or in a hardware/cloud signing service; they must never be committed to this repository. Each release also includes a SHA-256 checksum file.

# Automatic updates

The updater downloads only the latest GitHub Release package and its published SHA-256 file. It rejects mismatched checksums and unsafe ZIP paths, updates only a fixed allowlist of application files, and restores its backup if installation fails. User data under `%LOCALAPPDATA%\AutoElectiveOrb` is outside the update target.
