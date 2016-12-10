using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using Irony.Ast;
using Irony.Parsing;

using RAE.Game;

namespace RAE
{
    partial class Compiler
    {
        Article DoArticle(ParseTreeNode node)
        {
            if (node.Term.Name == "NONE")
                return Article.None;
            else
                return (Article)Enum.Parse(typeof(Article), node.Term.Name, true);
        }
    }
}
