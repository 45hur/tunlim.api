using System;

namespace tunlim.api
{
    internal class Mapping : Attribute
    {
        public string Map;
        public Mapping(string s)
        {
            Map = s;
        }
    }

    internal class PublicMapping : Attribute
    {
        public string Map;
        public PublicMapping(string s)
        {
            Map = s;
        }
    }
}