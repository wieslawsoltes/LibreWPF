import copy
import unittest
from contract import interior, native_plan, validate_snapshot, validate_native, validate_completion, SCENARIOS

def box(x=10, y=10, w=100, h=100):
    return dict(left=x, top=y, right=x+w, bottom=y+h)

def sample():
    def surface(handle):
        return dict(nativeHandle=handle, title=str(handle), presentedFrames=2, renderer="NativeMilWgpu",
                    inputAllowed=handle==2, sourceEnabled=handle==2, dpiX=1, dpiY=1,
                    clientWidth=100, clientHeight=100, client=box())
    return dict(schema="caption-native-input-observer-v1", pid=123, run="owned", sequence=2,
                phase="modal", index=0, elapsedSeconds=2, owner=surface(1), ownerTarget=box(20,20,20,20),
                ownerInput=dict(clicks=1,down=1,up=1,keys=0),
                dialog=dict(surface=surface(2), buttons="OK", closeEnabled=True, caption=box(10,10,100,32),
                            dragTarget=box(10,10,60,32), closeTarget=box(78,10,32,32), responseTargets=[]))

class ContractTests(unittest.TestCase):
    def check(self, state): return validate_snapshot(state,123,"owned","NativeMilWgpu",1)
    def test_original_modal_admitted(self): self.check(sample())
    def test_restored_owner_admitted(self):
        s=sample();s.update(phase="owner",dialog=None);s['owner'].update(inputAllowed=True,sourceEnabled=True);self.check(s)
    def test_native_exact_admitted(self):
        s=sample(); validate_native(s,[dict(xid=i,pid=123,title=str(i),transientFor=1 if i==2 else None,client=dict(x=10,y=10,width=100,height=100)) for i in (1,2)])
    def test_wrong_native_owner_rejected(self):
        s=sample()
        with self.assertRaisesRegex(ValueError,'transient owner'):
            validate_native(s,[dict(xid=i,pid=123,title=str(i),transientFor=9,client=dict(x=10,y=10,width=100,height=100)) for i in (1,2)])
    def test_rejections(self):
        changes=[('pid',124),('run','foreign'),('sequence',1),('elapsedSeconds',55.001),('index',4),('phase','unknown')]
        for k,v in changes:
            with self.subTest(k=k):
                s=sample();s[k]=v
                with self.assertRaises(ValueError):self.check(s)
    def test_modal_guards(self):
        for group,key,value in [('owner','inputAllowed',True),('owner','nativeHandle',2),('owner','dpiX',float('nan')),
                                ('owner','renderer','ManagedPortable'),('dialog','closeEnabled',False),('dialog','buttons','YesNo')]:
            with self.subTest(key=key):
                s=sample();s[group][key]=value
                with self.assertRaises(ValueError):self.check(s)
    def test_target_outside_client_rejected(self):
        s=sample();s['dialog']['closeTarget']=box(500,500,32,32)
        with self.assertRaises(ValueError):self.check(s)
    def test_original_native_mismatch_rejected(self):
        s=sample()
        with self.assertRaises(ValueError):validate_native(s,[dict(xid=1,pid=123,title='1',client=dict(x=11,y=10,width=100,height=100))])
    def test_conservative_interior_does_not_change_source(self):
        b=box(1.25,2.75,10,10);before=copy.deepcopy(b)
        self.assertEqual(interior(b),dict(x=2,y=3,width=9,height=9));self.assertEqual(b,before)
    def test_no_integer_interior_rejected(self):
        with self.assertRaises(ValueError):interior(box(1.1,1.1,.1,.1))
    def test_plan_is_disabled_and_bounded(self):
        p=native_plan();self.assertFalse(p['executable']);self.assertTrue(p['approvalRequired'])
        self.assertEqual(p['plannedNativeEvents'],47);self.assertEqual(p['processSeconds'],60)
    def test_source_completion_deadline_is_independent(self):
        value=dict(pid=123,run='owned',completed=True,ownerClicks=5,elapsedSeconds=55,
                   cases=[dict(buttons=a,result=b) for a,b,_ in SCENARIOS])
        validate_completion(value,123,'owned')
        for elapsed in (55.00001,-1,float('nan'),float('inf'),True):
            with self.subTest(elapsed=elapsed),self.assertRaisesRegex(ValueError,'deadline'):
                validate_completion(value|{'elapsedSeconds':elapsed},123,'owned')

if __name__=='__main__':unittest.main()
