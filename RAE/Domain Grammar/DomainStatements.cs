using System;
using System.Reflection.Emit;

using Irony.Parsing;

using RAE.Game;

namespace RAE
{
    partial class Compiler
    {
        StatementInfo DoDialogStatement(ParseTreeNode node)
        {
            // load speaker
            Type speaker = DoExpr(node.ChildNodes[0]);
            if (!speaker.IsSubclassOf(typeof(Verbable)))
                DoError(node, "Object speaking must operate on a subtype of Verbable, not " + speaker.Name);

            // load expr as string
            Type dlgType = DoExpr(node.ChildNodes[1]);
            ConvertType(dlgType, typeof(string));

            // call Game.
            CurrentFunction.IL.Emit(OpCodes.Call,
                speaker.BaseType.GetMethod("Say",
                new Type[] { typeof(string) }));

            return StatementInfo.Continuous;
        }
    }
}
