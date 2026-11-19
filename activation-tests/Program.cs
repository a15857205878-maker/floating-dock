using FloatingDock;
var identity="Test-"+Guid.NewGuid().ToString("N");
if(InstanceActivation.Request(identity,0))throw new Exception("missing owner accepted request");
using var delivered=new ManualResetEventSlim();
using(var listener=new InstanceActivation(identity,()=>delivered.Set())){
 if(!InstanceActivation.Request(identity,0)||!delivered.Wait(2000))throw new Exception("running owner did not receive activation");
 delivered.Reset();
 if(!InstanceActivation.Request(identity,0)||!delivered.Wait(2000))throw new Exception("second activation lost");
}
if(InstanceActivation.Request(identity,0))throw new Exception("closed owner is still registered");
Console.WriteLine("PASS missing/closed owner and repeated activation without launching a duplicate app");
