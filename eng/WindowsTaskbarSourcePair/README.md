# Original source taskbar comparison

This diagnostic runs the nine original source transitions and final retirement
observation from `12739b51f9505b40b2c74e40cb6bac0fba179c07` against Microsoft WPF
and the exact whole-CI LibreWPF producer. It performs no input injection or
external-window manipulation. Original 20-second internal and 30-second process
bounds remain unchanged; the internal clock does not prove startup duration.

`original.cs` is the exact Git source (LF SHA256
`02aa891b89856fdd6d27215281f00c9b88825088c0e965cec37f3cf958fddc2b`).
The successful Microsoft run 39028587600 compiled its CRLF representation
(SHA256 `052bb795e5c0d1ad35b20d135b0b61eff7903436d23f767d1462e5337b5195af`).
`source.py` reconstructs those bytes, applies only declared read-only handle and
compiler observations, then reverses them to prove all original actions remain.
The exact source observer compiles against the real local source assemblies;
that build is not Windows execution or package qualification.

`reference-observations.json` retains the original values from artifact 12099736557 of that
original Microsoft run (Git normalizes its line endings). It exercises comparator rejection controls only. Each
paired run executes a fresh Microsoft reference; the historical JSON never
substitutes for runtime results.

The comparison retains all raw process windows and checks every style bit,
visibility, enabled state and owner for the original three source windows and
retained taskbar owner. Provider-private infrastructure must remain identical
to its initial same-process/thread inventory during the transitions; final
cleanup may remove it but cannot add or change windows. Its implementation-
specific layout is not claimed as cross-provider parity. All four controlled
windows must retire at their original boundaries.

Both package and runtime identities are verified, including all selected product
packages, native assets and the actual three hosts' packaged DXC library.
This diagnostic does not replace the original modal input, caption or pixel
acceptance and cannot independently authorize a release.
