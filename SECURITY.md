# Security Policy

## Reporting a vulnerability

Please do **not** report security vulnerabilities through public GitHub issues,
pull requests or discussions.

Instead, report them privately using GitHub's private vulnerability reporting:
open the [Security tab](https://github.com/diagridio/dotnet-ai/security) of this
repository and click **Report a vulnerability**. See GitHub's documentation on
[privately reporting a security vulnerability](https://docs.github.com/en/code-security/security-advisories/guidance-on-reporting-and-writing-information-about-vulnerabilities/privately-reporting-a-security-vulnerability)
for details.

Please include as much of the following as you can:

- The affected NuGet package (`Diagrid.AI.Microsoft.AgentFramework` or
  `Diagrid.AI.Identity`) and version
- A description of the vulnerability and its impact
- Steps to reproduce, or a proof of concept
- Any known mitigation or workaround

We will acknowledge your report, investigate it, and keep you informed of our
progress while we work on a fix. Please give us a reasonable amount of time to
release a fix before disclosing the issue publicly.

## Supported versions

Security fixes are made on the latest `1.x` release of each package published
from this repository and shipped in a new release:

- [`Diagrid.AI.Microsoft.AgentFramework`](https://www.nuget.org/packages/Diagrid.AI.Microsoft.AgentFramework)
- [`Diagrid.AI.Identity`](https://www.nuget.org/packages/Diagrid.AI.Identity)

Older versions do not receive fixes, so please upgrade to the latest release.
