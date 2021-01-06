using System;

namespace fissure
{
    abstract record Location
    {
        
    }

    record ConcreteLocation : Location
    {
        public string FileName;
        public int Row;
        public int Column;
        public int Index;

        public ConcreteLocation(string fileName, int row, int column, int index)
        {
            FileName = fileName;
            Row = row;
            Column = column;
            Index = index;
        }

        public override string ToString() => $"{FileName}:{Row}:{Column}";
    }

    record GeneratedLocation : Location
    {
        public string FileName;

        public GeneratedLocation(string fileName)
        {
            FileName = fileName;
        }

        public override string ToString() => "Generated";
    }
}