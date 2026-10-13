using System;
using System.Collections.Generic;

namespace RimUnity.Serialization
{
    //资源载荷统一使用成员编码，以便保留显式类型及跨资源引用。
    [Serializable]
    public sealed class ResourceData
    {
        public int formatVersion = 1;
        public string type, name;
        public string colorSpace = "Gamma";
        public List<MemberData> members = new List<MemberData>();
    }
}
