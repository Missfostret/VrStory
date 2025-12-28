using UnityEngine;

namespace Dialogue.Scripts
{
   public struct SourceInfo
   {
      public string NodeName;
      public int LineNumber; // 1-based line in .dlg files
      public string RawLine;

      public SourceInfo(string nodeName, int lineNumber, string rawLine)
      {
         NodeName = nodeName;
         LineNumber = lineNumber;
         RawLine = rawLine;
      }

      public override string ToString() => $"{NodeName}:{LineNumber}";
   }
}
