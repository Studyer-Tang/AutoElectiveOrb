# Security and privacy

## Local data

- The unified-authentication password and TTShitu password are stored through Windows Credential Manager. The non-secret TTShitu username is stored in local UI settings.
- Credentials are passed only to the local Python child process through environment variables and are never written to repository files, JSON settings, INI configuration, or logs.
- After explicit opt-in in the settings window, captcha images and TTShitu credentials are sent over HTTPS to `api.ttshitu.com`. School credentials, cookies, tokens, course data, and request headers are never included in that request.
- Captcha images and recognized captcha text are not persisted locally. TTShitu is an independent third party; review its privacy policy, terms, retention practices, and pricing before use.
- Runtime settings, logs, caches, and swap history are stored under `%LOCALAPPDATA%\AutoElectiveOrb` and are excluded from Git.
- Request and response bodies are not dumped unless a developer explicitly sets `AUTOELECTIVE_ALLOW_SENSITIVE_DUMPS=1`.

Before reporting a bug, remove student IDs, TTShitu account details, cookies, tokens, course selections, and other personal information from screenshots and logs.

## Reporting a vulnerability

Please open a GitHub security advisory instead of publishing credentials or an exploitable security issue in a public issue.

# Release signing

Tagged releases support Windows Authenticode signing when a trusted certificate is configured. Unsigned releases are explicitly marked inside the package and may trigger an unknown-publisher warning. Signing keys and certificate passwords must be stored only as encrypted GitHub Actions secrets or in a hardware/cloud signing service; they must never be committed to this repository. Each release also includes a SHA-256 checksum file.

# Automatic updates

The updater downloads only the latest GitHub Release package and its published SHA-256 file. It rejects mismatched checksums and unsafe ZIP paths, updates only a fixed allowlist of application files, and restores its backup if installation fails. User data under `%LOCALAPPDATA%\AutoElectiveOrb` is outside the update target.
