using System;
using System.Collections;
using System.IO;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

// Exercise the runtime's actual geometry builder without creating native engine objects.
string root = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory,"../../../../../1.7/Assemblies"));
string game = args.Length > 1 ? args[1] : Environment.GetEnvironmentVariable("RIMWORLD_DIR");
string harmony = args.Length > 2 ? args[2] : Environment.GetEnvironmentVariable("HARMONY_PATH");
if (string.IsNullOrWhiteSpace(game) || !File.Exists(Path.Combine(game,"Waylith.dll")))
    throw new ArgumentException("Pass runtime/game/Harmony paths as arguments, or set RIMWORLD_DIR and HARMONY_PATH.");
game = Path.GetFullPath(game);
if (string.IsNullOrWhiteSpace(harmony) || !File.Exists(harmony))
    throw new ArgumentException("Pass the Harmony DLL as argument 3, or set HARMONY_PATH.");
harmony = Path.GetFullPath(harmony);
AssemblyLoadContext.Default.Resolving += (_, name) => {
    foreach (string folder in new[] { root, game }) {
        string path = Path.Combine(folder, name.Name + ".dll");
        if (File.Exists(path)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.GetFullPath(path));
    }
    if (name.Name == "0Harmony") return AssemblyLoadContext.Default.LoadFromAssemblyPath(harmony);
    return null;
};
var assembly = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root, "RimUnity.dll"));
var trailType = assembly.GetType("RimUnity.TrailRenderer", true);
var core = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(root, "RimUnity.Core.dll"));
var vectorType = core.GetType("RimUnity.Vector3", true);
const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
int triangles = 0;
foreach (var path in new[] {
    new[] {new Vector3(0,0,0),new Vector3(2,0,0)},
    new[] {new Vector3(0,0,0),new Vector3(2,0,0),new Vector3(2,2,0)},
    new[] {new Vector3(0,0,0),new Vector3(2,0,0),new Vector3(2,-2,0)}
}) foreach (bool rounded in new[] {false,true}) {
    object trail = RuntimeHelpers.GetUninitializedObject(trailType);
    foreach (string name in new[] {"vertices","normals","tangents","uv","colors","indices","sections","visible"}) {
        var field=trailType.GetField(name,flags);
        field.SetValue(trail,Activator.CreateInstance(field.FieldType));
    }
    trailType.GetField("widthMultiplier").SetValue(trail,1f);
    var curveType=trailType.GetField("widthCurve").FieldType;
    trailType.GetField("widthCurve").SetValue(trail,curveType.GetMethod("Constant").Invoke(null,new object[]{0f,1f,1f}));
    trailType.GetField("colorGradient").SetValue(trail,Activator.CreateInstance(trailType.GetField("colorGradient").FieldType));
    trailType.GetField("textureScale").SetValue(trail,Activator.CreateInstance(trailType.GetField("textureScale").FieldType,1f,1f));
    trailType.GetField("numCornerVertices").SetValue(trail,rounded?4:0);
    trailType.GetField("numCapVertices").SetValue(trail,rounded?4:0);
    var visible=(IList)trailType.GetField("visible",flags).GetValue(trail);
    var pointType=trailType.GetNestedType("TrailPoint",BindingFlags.NonPublic);
    foreach(var p in path) {
        object point=Activator.CreateInstance(pointType);
        pointType.GetField("position",flags).SetValue(point,Activator.CreateInstance(vectorType,p.X,p.Y,p.Z));
        visible.Add(point);
    }
    trailType.GetMethod("Build",flags).Invoke(trail,new object[]{null});
    var vertices=(IList)trailType.GetField("vertices",flags).GetValue(trail);
    var indices=(IList)trailType.GetField("indices",flags).GetValue(trail);
    Vector3 Read(int index) {
        object v=vertices[index];
        return new Vector3((float)vectorType.GetField("x").GetValue(v),(float)vectorType.GetField("y").GetValue(v),(float)vectorType.GetField("z").GetValue(v));
    }
    for(int i=0;i<indices.Count;i+=3) {
        Vector3 a=Read((int)indices[i]),b=Read((int)indices[i+1]),c=Read((int)indices[i+2]);
        float facing=Vector3.Dot(Vector3.Cross(b-a,c-a),Vector3.UnitZ);
        if(facing>1e-6f) throw new Exception($"Back-facing triangle: rounded={rounded}, pathEnd={path[^1]}, triangle={i/3}, dot={facing}");
        if(facing < -1e-6f) triangles++;
    }
}
Console.WriteLine($"PASS: {triangles} camera-facing strip/corner/cap triangles in straight and left/right paths.");

// Refraction copies scene color; it must not contribute that background as emission to Bloom.
AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game,"RimWorld.dll"));
var waylith=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(game,"Waylith.dll"));
waylith.GetType("Waylith.Rendering.ShaderSourceLoader",true).GetMethod("RegisterShaderSourceDirectory")
    .Invoke(null,new object[]{Path.Combine(game,"Rendering/Shaders")});
var shaderType=waylith.GetType("Waylith.Rendering.Shader",true);
var bindingType=waylith.GetType("Waylith.Rendering.MaterialBinding",true);
var drawType=waylith.GetType("Waylith.Rendering.FinalDrawItem",true);
object MakeDraw(bool refraction) {
    object shader=RuntimeHelpers.GetUninitializedObject(shaderType);
    shaderType.GetField("sceneGrabResolved",flags).SetValue(shader,true);
    if(refraction) shaderType.GetField("sceneGrabPoint",flags).SetValue(shader,
        RuntimeHelpers.GetUninitializedObject(waylith.GetType("Waylith.Rendering.SceneGrabPoint",true)));
    object binding=RuntimeHelpers.GetUninitializedObject(bindingType);
    bindingType.GetProperty("Shader").SetValue(binding,shader);
    object draw=RuntimeHelpers.GetUninitializedObject(drawType);
    drawType.GetField("binding",flags).SetValue(draw,binding);
    drawType.GetProperty("Layer").SetValue(draw,29);
    return draw;
}
var capture=assembly.GetType("RimUnity.Rendering.HdrDrawFrame",true).GetMethod("ShouldCapture",BindingFlags.Static|flags);
bool Include(object draw,int mask,bool bloom)=>(bool)capture.Invoke(null,new object[]{draw,mask,bloom});
object distortion=MakeDraw(true),emission=MakeDraw(false);
if(!Include(distortion,-1,false)) throw new Exception("Refraction missing from normal scene.");
if(Include(distortion,-1,true)) throw new Exception("Refraction background incorrectly included in Bloom emission.");
if(!Include(emission,1<<29,true)) throw new Exception("Colored trail emission missing from selected Bloom layer.");
if(Include(emission,1,true)) throw new Exception("Unselected layer leaked into Bloom.");
Console.WriteLine("PASS: refraction retained in scene and excluded from Bloom; emission and layer masks preserved.");

// A registered grab point can outlive its global property binding. Materials must
// keep the canonical texture object so native scene capture can populate it later.
var registry=waylith.GetType("Waylith.Rendering.SceneGrabRegistry",true);
var grabPoint=registry.GetMethod("Define").Invoke(null,new object[]{"_TrailGrabBindingTest",3001});
var grabType=grabPoint.GetType();
object GrabMember(string name)=>grabType.GetProperty(name)?.GetValue(grabPoint)??grabType.GetField(name).GetValue(grabPoint);
int grabId=(int)GrabMember("TexturePropertyId");
object grabTexture=GrabMember("Texture");
var globals=waylith.GetType("Waylith.Rendering.GlobalShaderState",true);
var globalTextureSetter=Array.Find(globals.GetMethods(),m=>m.Name=="SetTexture"&&m.GetParameters()[0].ParameterType==typeof(int));
globalTextureSetter.Invoke(null,new object[]{grabId,null});
object engineShader=RuntimeHelpers.GetUninitializedObject(shaderType);
shaderType.GetField("sceneGrabResolved",flags).SetValue(engineShader,true);
shaderType.GetField("sceneGrabPoint",flags).SetValue(engineShader,grabPoint);
var parser=waylith.GetType("Waylith.Rendering.ShaderMetadataParser",true);
object grabLayout=parser.GetMethod("ExtractMaterialLayout",new[]{typeof(string),typeof(string),typeof(int)}).Invoke(null,new object[]{
    "layout(set = 2, binding = 1) uniform sampler2D _TrailGrabBindingTest;", "TrailGrabBindingTest",2});
shaderType.GetProperty("MaterialLayout").SetValue(engineShader,grabLayout);
var wrapperShaderType=core.GetType("RimUnity.Shader",true);
object wrapperShader=wrapperShaderType.GetMethod("FromEngineShader").Invoke(null,new object[]{"TrailGrabBindingTest",engineShader});
var materialType=core.GetType("RimUnity.Material",true);
object material=Activator.CreateInstance(materialType,new[]{wrapperShader});
object Native(object value)=>materialType.GetProperty("engineMaterial").GetValue(value);
var nativeMaterialType=waylith.GetType("Waylith.Rendering.Material",true);
var getTexture=nativeMaterialType.GetMethod("GetTexture",new[]{typeof(int)});
var resolver=waylith.GetType("Waylith.Rendering.MaterialBindingResolver",true);
void CheckResolved(object native,string stage) {
    object[] resolved={native,null,null,null};
    resolver.GetMethod("ResolveMaterialBindingData").Invoke(null,resolved);
    object resolvedGrab=resolved[2].GetType().GetProperty("Item").GetValue(resolved[2],new object[]{0});
    if(!ReferenceEquals(resolvedGrab,grabTexture))
        throw new Exception(stage+": final authored sampler lost the canonical grab texture.");
}
void CheckGrab(object value,string stage) {
    if(!ReferenceEquals(getTexture.Invoke(Native(value),new object[]{grabId}),grabTexture))
        throw new Exception(stage+": canonical scene-grab texture missing from material binding.");
    CheckResolved(Native(value),stage);
}
CheckGrab(material,"creation after global binding loss");
object clone=Activator.CreateInstance(materialType,new[]{material});
CheckGrab(clone,"clone");
materialType.GetMethod("CopyPropertiesFromMaterial").Invoke(clone,new[]{material});
CheckGrab(clone,"property copy");
object plainEngineShader=RuntimeHelpers.GetUninitializedObject(shaderType);
shaderType.GetField("sceneGrabResolved",flags).SetValue(plainEngineShader,true);
shaderType.GetProperty("MaterialLayout").SetValue(plainEngineShader,grabLayout);
object plainShader=wrapperShaderType.GetMethod("FromEngineShader").Invoke(null,new object[]{"TrailPlainShaderTest",plainEngineShader});
object plainMaterial=Activator.CreateInstance(materialType,new[]{plainShader});
materialType.GetMethod("CopyPropertiesFromMaterial").Invoke(clone,new[]{plainMaterial});
CheckGrab(clone,"copy from a non-grab material");
materialType.GetProperty("shader").SetValue(clone,plainShader);
materialType.GetProperty("shader").SetValue(clone,wrapperShader);
CheckGrab(clone,"shader switch");
var passShaders=Array.CreateInstance(wrapperShaderType,2);
passShaders.SetValue(plainShader,0); passShaders.SetValue(wrapperShader,1);
materialType.GetMethod("SetPasses").Invoke(plainMaterial,new object[]{passShaders});
object nativePass=materialType.GetMethod("GetNativePass").Invoke(plainMaterial,new object[]{1});
CheckResolved(nativePass,"additional grab pass after property synchronization");
globalTextureSetter.Invoke(null,new[]{(object)grabId,grabTexture});
Console.WriteLine("PASS: authored grab sampler resolves after creation, clone, copy, shader switch and additional-pass synchronization without a global binding.");

// Particle trails use a lifetime multiplier, not absolute seconds (pillar-collapse shards).
var particleSystemType=assembly.GetType("RimUnity.ParticleSystem",true);
object particleSystem=Activator.CreateInstance(particleSystemType);
// The actual empty-event dispatch must not allocate once JIT/static initialization is warm.
var dispatch = particleSystemType.GetMethod("DispatchPhysicsEvents", flags).CreateDelegate<Action>(particleSystem);
for (int i = 0; i < 10000; i++) dispatch();
long dispatchBytes = GC.GetAllocatedBytesForCurrentThread();
for (int i = 0; i < 10000; i++) dispatch();
dispatchBytes = GC.GetAllocatedBytesForCurrentThread() - dispatchBytes;
if (dispatchBytes != 0) throw new Exception($"Empty particle event dispatch allocated {dispatchBytes} bytes.");
var collisionRecords = (IList)particleSystemType.GetField("collisionEvents", flags).GetValue(particleSystem);
var collisionRecordType = collisionRecords.GetType().GetGenericArguments()[0];
collisionRecords.Add(Activator.CreateInstance(collisionRecordType));
dispatch();
if (collisionRecords.Count != 1) throw new Exception("Dispatch changed collision records needed by GetCollisionEvents.");
collisionRecords.Clear(); dispatch();
Console.WriteLine("PASS: actual empty particle collision/trigger dispatch allocates 0 B across 10000 calls; nonempty collision records preserved.");
object trailModule=particleSystemType.GetField("trails").GetValue(particleSystem);
void TrailField(string name,object value)=>trailModule.GetType().GetField(name).SetValue(trailModule,value);
TrailField("enabled",true); TrailField("ratio",1f); TrailField("inheritParticleColor",false);
TrailField("sizeAffectsWidth",false); TrailField("worldSpace",false);
var lifetimeField=trailModule.GetType().GetField("lifetime");
lifetimeField.SetValue(trailModule,Activator.CreateInstance(lifetimeField.FieldType,new object[]{.05f}));
var particleType=particleSystemType.GetNestedType("Particle");
object particle=Activator.CreateInstance(particleType);
void ParticleField(string name,object value)=>particleType.GetField(name,flags).SetValue(particle,value);
ParticleField("identity",1UL); ParticleField("randomSeed",1U);
ParticleField("startLifetime",5f); ParticleField("remainingLifetime",5f);
ParticleField("position",Activator.CreateInstance(vectorType,0f,0f,0f));
particleSystemType.GetMethod("RecordTrail",flags).Invoke(particleSystem,new[]{particle});
var states=(IDictionary)particleSystemType.GetField("trailStates",flags).GetValue(particleSystem);
object state=states[1UL];var points=(IList)state.GetType().GetField("points",flags).GetValue(state);
float PointRemaining(int index)=>(float)points[index].GetType().GetField("remaining",flags).GetValue(points[index]);
if(Math.Abs(PointRemaining(0)-.25f)>.00001f)throw new Exception("Particle trail lifetime must be .05 * 5 seconds, got "+PointRemaining(0));
particleSystemType.GetMethod("AgeTrails",flags).Invoke(particleSystem,new object[]{.05f});
ParticleField("position",Activator.CreateInstance(vectorType,1f,0f,0f));
particleSystemType.GetMethod("RecordTrail",flags).Invoke(particleSystem,new[]{particle});
if(points.Count!=2 || Math.Abs(PointRemaining(0)-.20f)>.00001f || Math.Abs(PointRemaining(1)-.25f)>.00001f)
    throw new Exception("Old points must age; freshly recorded points retain their full lifetime.");
particleSystemType.GetMethod("AgeTrails",flags).Invoke(particleSystem,new object[]{.26f});
if(points.Count!=0)throw new Exception("Expired trail points retained.");
Console.WriteLine("PASS: particle trail lifetime follows particle starting lifetime, old/new samples age separately and expired points are removed.");
