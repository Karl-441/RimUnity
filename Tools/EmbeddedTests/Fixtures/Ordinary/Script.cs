using System;
using Verse;

namespace Fixture;

//故意使用普通 DLL，不导入内置 targets；模拟已有第三方模组。
public sealed class OrdinaryScript : RimUnity.MonoBehaviour { }

public sealed class OrdinaryMod : Mod
{
    public OrdinaryMod(ModContentPack content) : base(content)
    {
        //在首次 Mod 工厂调用期间就访问运行时，不能等到 XML 前才恢复失败的类型。
        if (typeof(OrdinaryScript).BaseType != typeof(RimUnity.MonoBehaviour))
            throw new InvalidOperationException("Ordinary DLL type identity mismatch");
        AppDomain.CurrentDomain.SetData("RimUnity.Test.OrdinaryConstructed", RimUnity.RuntimeInfo.Version);
    }
}
