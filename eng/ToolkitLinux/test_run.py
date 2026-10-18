import unittest
import run

GOOD = """[WGPUCONTEXT] Adapter 'Mesa', backend=OpenGles, requestedBackend=OpenGL, type=CPU,
ProGPU WPF Toolkit live input validation succeeded: logical 980x640, pixels 980x640, viewport 980x640@0,0, dpi 1.
ProGPU WPF Toolkit live input validation details: floating document window, anchorable hide/show, auto-hide side groups, layout serialization updated.
"""

class OriginalResultTests(unittest.TestCase):
    def test_complete_original_log(self):
        self.assertTrue(run.validate_log(GOOD)['passed'])
    def test_actual_wrong_backend(self):
        self.assertFalse(run.validate_log(GOOD.replace('backend=OpenGles', 'backend=Vulkan'))['passed'])
    def test_no_actual_backend(self):
        self.assertFalse(run.validate_log(GOOD.split('\n',1)[1])['passed'])
    def test_only_one_of_multiple_devices_matches(self):
        self.assertFalse(run.validate_log(GOOD+"[WGPUCONTEXT] Adapter 'other', backend=Vulkan, requestedBackend=Vulkan,")['passed'])
    def test_requested_default_is_not_forced_gl(self):
        self.assertFalse(run.validate_log(GOOD.replace('requestedBackend=OpenGL','requestedBackend=All'))['passed'])
    def test_partial_validation_is_not_success(self):
        self.assertFalse(run.validate_log(GOOD.replace('layout serialization updated', 'interrupted'))['passed'])
    def test_shifted_viewport(self):
        self.assertFalse(run.validate_log(GOOD.replace('@0,0','@1,0'))['passed'])
    def test_small_surface(self):
        self.assertFalse(run.validate_log(GOOD.replace('logical 980x640','logical 1000x700'))['passed'])
    def test_duplicate_success(self):
        self.assertFalse(run.validate_log(GOOD+GOOD)['passed'])

if __name__=='__main__': unittest.main()
