using System;

using Irony.Parsing;

namespace RAE
{
    [Language("RAD", "1.4", "Room-based Adventure Definition Language")]
    public class RAEGrammer : Grammar
    {
        public static NonTerminal NONE_TOKEN;

        public RAEGrammer()
            : base(false)
        {
            GrammarComments = "A language for text-based interactive fiction games.";
            //LanguageFlags = Irony.Parsing.LanguageFlags.CreateAst;

            #region Non-lexical

            var COMMENT = new CommentTerminal("LINE COMMENT", "//", "\n", "\r");
            var BLOCK_COMMENT = new CommentTerminal("BLOCK COMMENT", "/*", "*/");
            NonGrammarTerminals.Add(COMMENT);
            NonGrammarTerminals.Add(BLOCK_COMMENT);

            var NONE = new NonTerminal("NONE", Empty);
            NONE_TOKEN = NONE;

            #endregion

            #region Identifiers, Types, and Values

            var IDENTIFIER = new IdentifierTerminal("ID");
            var TYPE = new NonTerminal("TYPE");
            var ARRAY_TYPE = new NonTerminal("ARRAY TYPE");
            var GENERIC_TYPE = new NonTerminal("GENERIC TYPE");
            var TYPE_LIST = new NonTerminal("TYPE LIST");
            var TYPE_MULTI = new NonTerminal("TYPE MULTI");
            var INSTANCIABLE_TYPE = new NonTerminal("INSTANCIABLE TYPE");
            var VOID = ToTerm("VOID");
            var TYPE_OR_VOID = new NonTerminal("TYPE OR VOID");
            var PRIM_TYPE = new NonTerminal("PRIM TYPE");
            var GAME_TYPE = new NonTerminal("GAME TYPE");
            var INT_NUMBER = new NumberLiteral("INT NUMBER", NumberOptions.IntOnly | NumberOptions.AllowSign);
            var FLOAT_NUMBER = new NumberLiteral("FLOAT NUMBER", NumberOptions.AllowStartEndDot | NumberOptions.AllowSign);
            INT_NUMBER.DefaultIntTypes = new TypeCode[] { TypeCode.Int32, TypeCode.Int64 };
            FLOAT_NUMBER.DefaultFloatType = TypeCode.Double;
            FLOAT_NUMBER.AddExponentSymbols("eE", TypeCode.Double);
            var STRING_LITERAL = new StringLiteral("STRING", "\"", StringOptions.AllowsAllEscapes);
            var COMMA = ToTerm(",");
            var SEMI = ToTerm(";");
            var CONSTANTS = new NonTerminal("CONSTANTS");
            var OPTIONAL_HIDDEN = new NonTerminal("OPTIONAL HIDDEN");

            TYPE.Rule = PRIM_TYPE | INSTANCIABLE_TYPE | ARRAY_TYPE;
            INSTANCIABLE_TYPE.Rule = GAME_TYPE | IDENTIFIER | GENERIC_TYPE;
            ARRAY_TYPE.Rule = TYPE + PreferShiftHere() + "[]";
            GENERIC_TYPE.Rule = IDENTIFIER + "#" + TYPE_LIST;
            TYPE_LIST.Rule = TYPE | ("(" + TYPE_MULTI + ")");
            TYPE_MULTI.Rule = MakePlusRule(TYPE_MULTI, COMMA, TYPE);
            TYPE_OR_VOID.Rule = TYPE | VOID;
            PRIM_TYPE.Rule = ToTerm("INT") | "STRING" | "DOUBLE" | "BOOL";
            GAME_TYPE.Rule = ToTerm("GAME") | "ITEM" | "ROOM" | "SPOT" | "VERBABLE";

            CONSTANTS.Rule = ToTerm("TRUE") | "FALSE" | "NULL" | "THIS" | "GAME" | "PLAYER";

            OPTIONAL_HIDDEN.Rule = Empty | "HIDDEN";

            RegisterBracePair("{", "}");
            RegisterBracePair("(", ")");
            RegisterBracePair("[", "]");
            MarkPunctuation(";", "(", ")", "{", "}", "[", "]", ":", "#", "?");

            #endregion

            #region Operators

            var BINARY_OP = new NonTerminal("BINARY OP", "operator");
            var UNARY_PRE_OP = new NonTerminal("UNARY PRE OP", "operator");
            var ASSIGNMENT_OP = new NonTerminal("ASSIGNMENT OP");
            var SELF_ASSIGNMENT_OP = new NonTerminal("SELF ASSIGNMENT OP");
            var OBJ_OP = new NonTerminal("OBJECT OP");
            var OBJ_EXT_OP = new NonTerminal("OBJECT EXT OP");
            var OBJ_OP_PAR_ID = new NonTerminal("OBJECT OP PAR ID");
            var ARTICLE = new NonTerminal("ARTICLE");

            SELF_ASSIGNMENT_OP.Rule = ToTerm("++") | "--" | "!!";
            UNARY_PRE_OP.Rule = ToTerm("-") | "!" | "~";
            BINARY_OP.Rule = ToTerm("+") | "-" | "*" | "/" | "%" | "|" | "&" | "^" | ">" | "<" | ">=" | "<=" | "||" | "&&" | "==" | "!=" | "<<" | ">>";
            ASSIGNMENT_OP.Rule = ToTerm("=") | "+=" | "-=" | "*=" | "/=" | "%=" | "|=" | "&=" | "^=" | "<<=" | ">>=";
            OBJ_OP.Rule = ToTerm("AKA") | "GOTO" | "GIVE" | "TAKE" | "HAS" | "!HAS" | OBJ_EXT_OP;
            OBJ_EXT_OP.Rule = ToTerm("GESTURE");
            OBJ_OP_PAR_ID.Rule = ToTerm("TO") | "DO" | "IS" | "ISNT"; // these take a specially-resolved id instead of expr
            ARTICLE.Rule = ToTerm("A") | "THE" | "SOME" | "AN" | "YOUR" | "MY" | NONE;

            RegisterOperators(-1, "=", "+=", "-=", "*=", "/=", "%=", "|=", "&=", "^=", "<<=", ">>=");
            RegisterOperators(0, "?");
            RegisterOperators(1, "||");
            RegisterOperators(2, "&&");
            RegisterOperators(3, "|");
            RegisterOperators(4, "^");
            RegisterOperators(5, "&");
            RegisterOperators(6, "==", "!=");
            RegisterOperators(7, "<", ">", "<=", ">=");
            RegisterOperators(8, "<<", ">>");
            RegisterOperators(9, "+", "-");
            RegisterOperators(10, "*", "/", "%");

            #endregion

            #region Code Defs

            var PROGRAM_BODY = new NonTerminal("PROGRAM BODY");
            var PROGRAM_LINE = new NonTerminal("PROG LINE");
            var ROOM_LINE = new NonTerminal("ROOM LINE");
            var BLOCK_BODY = new NonTerminal("BLOCK BODY");
            var BLOCK_BODY_NO_PARAMS = new NonTerminal("BLOCK BODY");
            var BLOCK_SINGLE = new NonTerminal("BLOCK SINGLE");
            var BLOCK_SINGLE_STATEMENT_LINE = new NonTerminal("BLOCK SINGLE STATEMENT");
            var BLOCK_SINGLE_NO_PARAMS = new NonTerminal("BLOCK SINGLE");
            var BLOCK_MULTI = new NonTerminal("BLOCK MULTI");
            var OPTIONAL_ID = new NonTerminal("OPTIONAL ID");
            var OPTIONAL_SUBCLASS = new NonTerminal("OPTIONAL SUBCLASS");
            //var OPTIONAL_DEF_PARAM_LIST = new NonTerminal("OPTIONAL DEF PARAM LIST");
            var GAME = new NonTerminal("GAME");
            var ITEM = new NonTerminal("ITEM");
            var NPC = new NonTerminal("NPC");
            var ROOM = new NonTerminal("ROOM");
            var SPOT = new NonTerminal("SPOT");
            var SPOTST = new NonTerminal("SPOTST");
            var FN_DEF = new NonTerminal("FN DEF");
            var ON_VERB = new NonTerminal("ON VERB");
            var TICK = new NonTerminal("TICK");
            var STATE = new NonTerminal("STATE");
            var STATEMENT_BODY = new NonTerminal("STATEMENT BODY");
            var STATEMENT_LINE = new NonTerminal("STATEMENT LINE");
            var OPTIONAL_STATEMENT_LINE = new NonTerminal("OPT STATEMENT LINE");
            var OPTIONAL_EXPR = new NonTerminal("OPT EXPR");
            var DEF_PARAM_LIST = new NonTerminal("DEF PARAM LIST");
            var DEF_VARIABLE = new NonTerminal("DEF VARIABLE");
            var IF = new NonTerminal("IF");
            var IF_ELSE = new NonTerminal("ELSE BLK");
            var WHILE = new NonTerminal("WHILE");
            var FOR = new NonTerminal("FOR");
            var CLASS_CALL = new NonTerminal("CLASS CALL");
            var CLASS_STATIC_CALL = new NonTerminal("CLASS STATIC CALL");
            var CLASS_MEMBER = new NonTerminal("CLASS FIELD");
            var CLASS_STATIC_MEMBER = new NonTerminal("CLASS STATIC FIELD");
            var LOCAL_CALL = new NonTerminal("LOCAL CALL");
            var PARAM_LIST = new NonTerminal("PARAM LIST");
            var EXPR_STATEMENT = new NonTerminal("EXPRESSION STMT");
            var EXPR = new NonTerminal("EXPRESSION");
            var PRIMARY_EXPR = new NonTerminal("1RY EXPRESSION");
            var SECONDARY_EXPR = new NonTerminal("2RY EXPRESSION");
            var PAREN_EXPR = new NonTerminal("PAREN EXPR");
            var BINARY_EXPR = new NonTerminal("BINARY EXPR");
            var UNARY_EXPR = new NonTerminal("UNARY EXPR");
            var COND_EXPR = new NonTerminal("COND EXPR");

            var COMMAND_PARAM = new NonTerminal("COMMAND PARAM");
            var COMMAND_PARAM_SINGLE = new NonTerminal("CMD PARAM SINGLE");
            var COMMAND_PARAM_MULTI = new NonTerminal("CMD PARAM MULTI");
            var THIS_OBJ_COMMAND = new NonTerminal("THIS COMMAND");
            var THIS_OBJ_COMMAND_PAR_ID = new NonTerminal("THIS COMMAND PAR ID");
            var OBJ_COMMAND = new NonTerminal("OBJ COMMAND");
            var OBJ_COMMAND_PAR_ID = new NonTerminal("OBJ COMMAND PAR ID");

            var ASSIGNMENT = new NonTerminal("ASSIGNMENT");
            var SELF_ASSIGNMENT = new NonTerminal("SELF ASSIGNMENT");
            var ASSIGNMENT_DEST = new NonTerminal("ASSIGN DEST");
            var RETURN = new NonTerminal("RETURN");
            var BREAK = new NonTerminal("BREAK");
            var LOCAL_DEF = new NonTerminal("LOCAL DEF");
            var GLOBAL_DEF = new NonTerminal("GLOBAL DEF");
            var DEF_ACCESS = new NonTerminal("DEF ACCESS");
            var OPTIONAL_PARAMS = new NonTerminal("OPTIONAL PARAMS");
            var OPTIONAL_ASSIGNMENT = new NonTerminal("OPT ASSIGN");
            var USING_IDS = new NonTerminal("USING IDS");
            var USING = new NonTerminal("USING");
            var NEWOBJ = new NonTerminal("NEW OBJ");
            var NEWARRAY = new NonTerminal("NEW ARRAY");
            var NEWARRAY_PREFILL = new NonTerminal("NEW ARRAY PREFILL");
            var TRYCATCH = new NonTerminal("TRY");
            var CATCH_PARAM = new NonTerminal("CATCH PARAM");
            var THROW = new NonTerminal("THROW");
            var ARRAYREF = new NonTerminal("ARRAYREF");
            var SWITCH = new NonTerminal("SWITCH");
            var CASE = new NonTerminal("CASE");
            var CASE_LIST = new NonTerminal("CASE LIST");
            var OPT_DEFAULT = new NonTerminal("OPT DEFAULT");
            var DEFAULT = new NonTerminal("DEFAULT");
            var DIALOG_STATEMENT = new NonTerminal("DIALOG STATEMENT");
            var DIALOG_MENU = new NonTerminal("DIALOG MENU");
            var MENU_ITEM_LIST = new NonTerminal("MENU ITEM LIST");
            var MENU_ITEM = new NonTerminal("MENU ITEM");
            var CASE_IF = new NonTerminal("CASE IF");
            var OPT_ESCAPE = new NonTerminal("OPT ESCAPE");
            var ESCAPE = new NonTerminal("ESCAPE");
            var VERB = new NonTerminal("VERB");
            var ID_LIST = new NonTerminal("ID LIST");
            var THEN_COMMAND = new NonTerminal("THEN COMMAND");

            #endregion

            #region Code

            Root = PROGRAM_BODY;
            PROGRAM_BODY.Rule = MakeStarRule(PROGRAM_BODY, PROGRAM_LINE);
            PROGRAM_LINE.Rule = GAME | ITEM | NPC | ROOM | USING | VERB;
            BLOCK_BODY.Rule = BLOCK_SINGLE | BLOCK_MULTI;
            BLOCK_BODY_NO_PARAMS.Rule = BLOCK_SINGLE_NO_PARAMS | BLOCK_SINGLE | BLOCK_MULTI; // for "else", etc
            BLOCK_SINGLE.Rule = BLOCK_SINGLE_STATEMENT_LINE | SEMI;
            BLOCK_SINGLE_STATEMENT_LINE.Rule = ToTerm(":") + STATEMENT_LINE;
            BLOCK_SINGLE_NO_PARAMS.Rule = STATEMENT_LINE;
            BLOCK_MULTI.Rule = "{" + STATEMENT_BODY + "}";
            OPTIONAL_ID.Rule = IDENTIFIER | NONE;
            OPTIONAL_SUBCLASS.Rule = (ToTerm("#") + INSTANCIABLE_TYPE) | NONE;
            //OPTIONAL_DEF_PARAM_LIST.Rule = DEF_PARAM_LIST | NONE;

            GAME.Rule = ToTerm("GAME") + OPTIONAL_ID + BLOCK_BODY;
            ITEM.Rule = ToTerm("ITEM") + OPTIONAL_SUBCLASS + OPTIONAL_PARAMS + IDENTIFIER + ARTICLE + STRING_LITERAL + BLOCK_BODY;
            NPC.Rule = ToTerm("NPC") + OPTIONAL_SUBCLASS + OPTIONAL_PARAMS + IDENTIFIER + ARTICLE + STRING_LITERAL + BLOCK_BODY;
            ROOM.Rule = ToTerm("ROOM") + OPTIONAL_SUBCLASS + OPTIONAL_PARAMS + IDENTIFIER + ARTICLE + STRING_LITERAL + BLOCK_BODY;
            SPOT.Rule = ToTerm("SPOT") + PreferShiftHere() + IDENTIFIER + ARTICLE + STRING_LITERAL + BLOCK_BODY;
            SPOTST.Rule = ToTerm("SPOT") + "#" + INSTANCIABLE_TYPE + OPTIONAL_PARAMS + IDENTIFIER + ARTICLE + STRING_LITERAL + BLOCK_BODY;
            STATEMENT_LINE.Rule = RETURN | BREAK | GLOBAL_DEF | LOCAL_DEF
                | WHILE | IF | FOR | SWITCH
                | TRYCATCH | THROW
                | DIALOG_STATEMENT | DIALOG_MENU
                | FN_DEF | ON_VERB | TICK | STATE | SPOT | SPOTST
                | EXPR_STATEMENT;
            OPTIONAL_STATEMENT_LINE.Rule = STATEMENT_LINE | SEMI;
            OPTIONAL_EXPR.Rule = EXPR | Empty;
            FN_DEF.Rule = ToTerm("FN") + TYPE_OR_VOID + IDENTIFIER + "(" + DEF_PARAM_LIST + ")" + BLOCK_BODY;
            STATE.Rule = ToTerm("STATE") + IDENTIFIER + BLOCK_BODY;
            STATEMENT_BODY.Rule = MakeStarRule(STATEMENT_BODY, STATEMENT_LINE);
            DEF_PARAM_LIST.Rule = MakeStarRule(DEF_PARAM_LIST, COMMA, DEF_VARIABLE);
            DEF_VARIABLE.Rule = TYPE + IDENTIFIER;
            WHILE.Rule = ToTerm("WHILE") + EXPR
                + BLOCK_BODY;
            IF.Rule = ToTerm("IF") + EXPR + BLOCK_BODY + IF_ELSE;
            IF_ELSE.Rule = Empty | (PreferShiftHere() + "ELSE" + BLOCK_BODY_NO_PARAMS);
            FOR.Rule = ToTerm("FOR") + OPTIONAL_STATEMENT_LINE + EXPR_STATEMENT + OPTIONAL_EXPR
                + BLOCK_BODY;
            CLASS_CALL.Rule = CLASS_MEMBER + "(" + PARAM_LIST + ")";
            CLASS_STATIC_CALL.Rule = CLASS_STATIC_MEMBER + "(" + PARAM_LIST + ")";
            CLASS_MEMBER.Rule = SECONDARY_EXPR + PreferShiftHere() + "." + IDENTIFIER; // resolve shift to class call
            CLASS_STATIC_MEMBER.Rule = TYPE + ".." + IDENTIFIER;
            LOCAL_CALL.Rule = IDENTIFIER + "(" + PARAM_LIST + ")";
            ON_VERB.Rule = ToTerm("ON") + IDENTIFIER + BLOCK_BODY;
            TICK.Rule = ToTerm("TICK") + BLOCK_BODY_NO_PARAMS;
            PARAM_LIST.Rule = MakeStarRule(PARAM_LIST, COMMA, EXPR);
            EXPR_STATEMENT.Rule = EXPR + SEMI;
            EXPR.Rule = PRIMARY_EXPR | BINARY_EXPR | COND_EXPR | ASSIGNMENT
                | THIS_OBJ_COMMAND | OBJ_COMMAND | THIS_OBJ_COMMAND_PAR_ID | OBJ_COMMAND_PAR_ID | THEN_COMMAND
                | NEWOBJ | NEWARRAY | NEWARRAY_PREFILL;
            PRIMARY_EXPR.Rule = INT_NUMBER | FLOAT_NUMBER | SECONDARY_EXPR;
            SECONDARY_EXPR.Rule = STRING_LITERAL // these expression forms split off so coder can't possibly do [number].fn() which is illegal
                | IDENTIFIER | CONSTANTS
                | CLASS_CALL | CLASS_STATIC_CALL | CLASS_MEMBER | CLASS_STATIC_MEMBER
                | LOCAL_CALL
                | PAREN_EXPR | UNARY_EXPR | SELF_ASSIGNMENT | ARRAYREF;
            PAREN_EXPR.Rule = "(" + EXPR + ")";
            BINARY_EXPR.Rule = EXPR + BINARY_OP + EXPR;
            UNARY_EXPR.Rule = UNARY_PRE_OP + PRIMARY_EXPR;
            COND_EXPR.Rule = EXPR + "?" + EXPR + ":" + EXPR;
            OBJ_COMMAND.Rule = SECONDARY_EXPR + THIS_OBJ_COMMAND;
            THIS_OBJ_COMMAND.Rule = OBJ_OP + COMMAND_PARAM;
            COMMAND_PARAM.Rule = COMMAND_PARAM_SINGLE | COMMAND_PARAM_MULTI;
            COMMAND_PARAM_SINGLE.Rule = EXPR;
            COMMAND_PARAM_MULTI.Rule = "{" + PARAM_LIST + "}";
            THIS_OBJ_COMMAND_PAR_ID.Rule = OBJ_OP_PAR_ID + IDENTIFIER;
            OBJ_COMMAND_PAR_ID.Rule = SECONDARY_EXPR + THIS_OBJ_COMMAND_PAR_ID;
            THEN_COMMAND.Rule = SECONDARY_EXPR + "THEN" + SECONDARY_EXPR;
            ASSIGNMENT.Rule = ASSIGNMENT_DEST + ASSIGNMENT_OP + EXPR;
            SELF_ASSIGNMENT.Rule = ASSIGNMENT_DEST + SELF_ASSIGNMENT_OP;
            ASSIGNMENT_DEST.Rule = IDENTIFIER | CLASS_MEMBER | ARRAYREF;
            RETURN.Rule = ToTerm("RETURN") + (Empty | EXPR) + SEMI;
            BREAK.Rule = ToTerm("BREAK") + SEMI;
            DEF_ACCESS.Rule = ToTerm("PUBLIC") | "PRIVATE";
            GLOBAL_DEF.Rule = DEF_ACCESS + TYPE + IDENTIFIER + OPTIONAL_ASSIGNMENT + SEMI;
            LOCAL_DEF.Rule = TYPE + IDENTIFIER + OPTIONAL_ASSIGNMENT + SEMI;
            OPTIONAL_ASSIGNMENT.Rule = Empty | (ToTerm("=") + EXPR);
            USING_IDS.Rule = MakePlusRule(USING_IDS, ToTerm("."), IDENTIFIER);
            ToTerm("USING").SetFlag(TermFlags.IsPunctuation); // required for marking USING transient
            USING.Rule = "USING" + USING_IDS + SEMI;
            OPTIONAL_PARAMS.Rule = NONE | ("(" + PARAM_LIST + ")");
            NEWOBJ.Rule = "NEW" + INSTANCIABLE_TYPE + OPTIONAL_PARAMS;
            NEWARRAY.Rule = "NEW" + TYPE + "[" + EXPR + "]";
            NEWARRAY_PREFILL.Rule = "NEW" + ARRAY_TYPE + "{" + PARAM_LIST + "}";
            TRYCATCH.Rule = "TRY" + BLOCK_BODY_NO_PARAMS + "CATCH" + CATCH_PARAM + BLOCK_BODY;
            CATCH_PARAM.Rule = TYPE | DEF_VARIABLE;
            THROW.Rule = "THROW" + EXPR + SEMI;
            ARRAYREF.Rule = SECONDARY_EXPR + PreferShiftHere() + "[" + EXPR + "]";
            SWITCH.Rule = "SWITCH" + EXPR + "{" + CASE_LIST + OPT_DEFAULT + "}";
            CASE_LIST.Rule = MakePlusRule(CASE_LIST, CASE);
            CASE.Rule = "CASE" + EXPR + ":" + STATEMENT_BODY;
            OPT_DEFAULT.Rule = DEFAULT | NONE;
            DEFAULT.Rule = ToTerm("DEFAULT") + ":" + STATEMENT_BODY;
            DIALOG_STATEMENT.Rule = SECONDARY_EXPR + ":" + EXPR + SEMI;
            DIALOG_MENU.Rule = ToTerm("MENU") + "{" + STATEMENT_BODY + MENU_ITEM_LIST + OPT_ESCAPE + "}";
            MENU_ITEM_LIST.Rule = MakeStarRule(MENU_ITEM_LIST, MENU_ITEM);
            MENU_ITEM.Rule = CASE | CASE_IF;
            CASE_IF.Rule = "CASE" + EXPR + "IF" + EXPR + ":" + STATEMENT_BODY;
            OPT_ESCAPE.Rule = ESCAPE | NONE;
            ESCAPE.Rule = ToTerm("ESCAPE") + EXPR + ":" + STATEMENT_BODY;
            VERB.Rule = ToTerm("VERB") + ID_LIST + BLOCK_BODY;
            ID_LIST.Rule = MakePlusRule(ID_LIST, COMMA, IDENTIFIER);

            #endregion

            #region Parser Extras
            MarkTransient(STATEMENT_LINE, ROOM_LINE, PROGRAM_LINE, EXPR, PRIMARY_EXPR, SECONDARY_EXPR, BINARY_OP, UNARY_PRE_OP, ASSIGNMENT_OP,
                SELF_ASSIGNMENT_OP, TYPE, TYPE_LIST, ASSIGNMENT_DEST, BLOCK_BODY, PAREN_EXPR, OBJ_OP, OBJ_EXT_OP, ARTICLE, BLOCK_MULTI, EXPR_STATEMENT,
                OPTIONAL_PARAMS, BLOCK_BODY_NO_PARAMS, OPTIONAL_STATEMENT_LINE, TYPE_OR_VOID, OBJ_OP_PAR_ID, OPT_DEFAULT, OPT_ESCAPE, INSTANCIABLE_TYPE,
                OPTIONAL_EXPR, BLOCK_SINGLE_STATEMENT_LINE, COMMAND_PARAM, COMMAND_PARAM_MULTI, OPTIONAL_ID, OPTIONAL_SUBCLASS, USING, DEF_ACCESS, CATCH_PARAM, MENU_ITEM);

            #endregion
        }
    }
}
