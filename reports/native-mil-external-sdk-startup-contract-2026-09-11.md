# External SDK startup contract qualification

The external no-source-change SDK harness initially stopped at a stale package
text assertion expecting a blanket Windows native activation rejection. The
bootstrap already uses the typed portable startup connection introduced in
0e88f0a2d and preserved by the canonical integration merge.

The harness now asserts the actual package contract: x64/ARM64 process admission,
explicit rejection of unsupported architectures, required typed activation, and
native media initialization before WPF module construction. Source inspection
confirms that ProGpuWpfNativeMediaServices.Initialize freezes portable media and
registers geometry/text/document services. No product startup code or admission
behavior changed. These assertions do not establish Windows runtime qualification.

The updated harness compiles and proceeds beyond package layout validation into
the external-consumer run. Full results and final integrated qualification remain
pending. It consumes the existing d669c28c0 package feed and exact b54db165 ProGPU
packages; it is not evidence of a newly rebuilt release bundle.
