# Security policy

Security fixes target the latest stable release. Development candidates must be
identified by source commit and gateway/plugin build identity when reporting a
problem. The release package's manifest states which AutoCAD plugin years it ships.

## Trust boundary

dwg-mcp connects a local stdio MCP client to an AutoCAD plugin through loopback TCP
or a local Named Pipe. Plugin requests require the session token from local
discovery data. Protect access to that data and to the Windows account running
AutoCAD; the token does not isolate other software running as that same user.

`dwg_send_code` runs trusted synchronous C# with the privileges of AutoCAD,
including access to drawing data and the filesystem. It is not a sandbox. Use
`--disable-send-code` or `--read-only` to remove it from the MCP surface; the
AutoCAD `MCPDISABLECODE` command also disables it for the current host session.
`dwg_run_lisp` refuses execution in this build. Do not bypass that refusal through
another tool.

Call logging is off by default. Opted-in logs redact code bodies and credentials
before persistence. Read-only mode excludes drawing writes, code execution and
image-file creation; permitted view navigation can still change transient UI state.

## Reporting a vulnerability

Avoid public issues for reports containing credentials, private drawings or exploit
details. Use [GitHub private vulnerability reporting](https://github.com/bimwright/dwg-mcp/security/advisories/new)
when it is available. Otherwise contact the maintainer privately using the email
in this repository's commit metadata.

Include the gateway/plugin versions and build identities, AutoCAD year,
reproduction steps, expected and actual behavior, and the affected trust boundary.
Use a disposable drawing and remove private project data from attachments.
