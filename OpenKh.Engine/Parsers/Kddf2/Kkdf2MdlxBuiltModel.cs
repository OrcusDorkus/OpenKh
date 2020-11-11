﻿using System;
using System.Collections.Generic;

namespace OpenKh.Engine.Parsers.Kddf2
{
    public class Kkdf2MdlxBuiltModel
    {
        // <textureIndex, isOpaque> -> Model
        public SortedDictionary<Tuple<int, bool>, Model> textureIndexBasedModelDict;
        public Kkdf2MdlxParser parser;
    }
}
