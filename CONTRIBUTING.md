# Contributing

Bug reports and verified device additions are welcome.

For a new ARDOR mouse, please include:

- the exact model and article number;
- wired and receiver hardware IDs from Windows Device Manager;
- a link to the official ARDOR/DNS software package;
- `--probe` output if a console build is available;
- whether the percentage was checked against the official software.

Do not guess a protocol from the sensor model alone. Battery commands must be
limited to confirmed VID/PID pairs and validated responses.

Build with `build.ps1` on Windows 10/11. Keep the application dependency-free
and do not add telemetry or network access to the runtime executable.
