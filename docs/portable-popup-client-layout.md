# Portable popup client layout

Original modal acceptance on qualified producer `ad86d510` passed the Microsoft
reference and the portable MessageBox, then stalled before the second dialog's
first observation. The input-traced diagnostic 39013095181 completed both dialogs
and popup dismissal, but exposed a separate context-menu frame mismatch: the
native client was 175 pixels wide while its source PopupRoot retained 174.8984375.
Microsoft's original context-menu source and native client were both 175. Preserve
both original failures; the traced run does not resolve the earlier stall.

`PopupSecurityHelper` previously arranged the root to its fractional measured
content, then sent an integer width to the host. Repeated source layout updates
could restore the fractional frame without changing the native size. PopupRoot
now recovers its natural content measurement even after a valid constrained
source layout, and uses the same existing nearest-integer, minimum-one client dimensions for
source arrangement and host allocation. Other visual roots retain their existing
measurement path. The original child's desired size, explicit dimensions and
paint remain unchanged; this does not round text widths, adjust glyph advances,
change the observer, or fit geometry to a screenshot. Content wider than a rounded
down allocation retains its existing render extent and clipping behavior.

A source regression reproduces the 174.8984375-versus-175 failure before the change;
the integral control passes. Controls cover repeated layout, the reported width,
an integral width, rounding down and an exact half. They check the allocated slot,
original desired size and unchanged child dimensions. They do not qualify native
pixels, DPI, package/source transport or application acceptance. Rebuilt whole
producer qualification and all original modal/Gallery checks remain required.

Local validation: all 45 popup ownership cases and the 16-case dismissal/client
layout gate pass after rebuilding the source test project. The initial
fractional-frame failure and an intermediate constrained-measure failure are
retained. Neither source result replaces the original hosted acceptance gates.
