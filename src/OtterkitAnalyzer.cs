using System.Text;

namespace Otterkit;

public static class Analyzer
{
    private static string FileName = string.Empty;
    private static string SourceId = string.Empty;
    private static string SourceType = string.Empty;
    private static string CurrentSection = string.Empty;

    public static List<Token> Analyze(List<Token> tokenList, string fileName)
    {
        FileName = fileName;

        List<Token> analyzed = new();
        int index = 0;

        Source();

        if (ErrorHandler.Error)
            ErrorHandler.Terminate("parsing");

        return analyzed;

        void Source()
        {
            if (CurrentEquals("EOF"))
            {
                analyzed.Add(Current());
                return;
            }

            IDENTIFICATION();
            if (CurrentEquals("ENVIRONMENT"))
                ENVIRONMENT();

            if (CurrentEquals("DATA"))
                DATA();

            PROCEDURE();

            if (CurrentEquals("IDENTIFICATION") || CurrentEquals("PROGRAM-ID") || CurrentEquals("FUNCTION-ID"))
                Source();
            
        }

        void IDENTIFICATION()
        {
            string headerPeriodError = """
            Missing separator period at the end of this IDENTIFICATION DIVISION header, every division header must end with a separator period
            """;

            if (CurrentEquals("IDENTIFICATION"))
            {
                Expected("IDENTIFICATION");
                Expected("DIVISION");
                Expected(".", headerPeriodError, -1, "separator period");
            }

            if (!CurrentEquals("PROGRAM-ID") && !CurrentEquals("FUNCTION-ID"))
            {
                string missingIdentificationError = """
                Missing source unit ID name (PROGRAM-ID, FUNCTION-ID, CLASS-ID...), the identification division header is optional but every source unit must still have an ID.
                """;

                ErrorHandler.Parser.Report(fileName, Current(), "general", missingIdentificationError);
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }

            if (CurrentEquals("PROGRAM-ID"))
                ProgramId();

            if (CurrentEquals("FUNCTION-ID"))
                FunctionId();
        }

        void ProgramId()
        {
            Expected("PROGRAM-ID");
            Expected(".");
            SourceId = Current().value;
            SourceType = "PROGRAM";
            Identifier();
            Expected(".");
        }

        void FunctionId()
        {
            Expected("FUNCTION-ID");
            Expected(".");
            SourceId = Current().value;
            SourceType = "FUNCTION";
            Identifier();
            Expected(".");
        }

        void ENVIRONMENT()
        {
            string headerPeriodError = """
            Missing separator period at the end of this ENVIRONMENT DIVISION header, every division header must end with a separator period
            """;

            Expected("ENVIRONMENT", "environment division");
            Expected("DIVISION");
            Expected(".", headerPeriodError, -1, "separator period");
        }

        void DATA()
        {
            string headerPeriodError = """
            Missing separator period at the end of this DATA DIVISION header, every division header must end with a separator period
            """;

            Expected("DATA", "data division");
            Expected("DIVISION");
            Expected(".", headerPeriodError, -1, "separator period");
            DataSections();
        }

        void DataSections()
        {

            if (CurrentEquals("WORKING-STORAGE"))
                WorkingStorage();

            if (CurrentEquals("LOCAL-STORAGE"))
                LocalStorage();

            if (CurrentEquals("LINKAGE"))
                LinkageSection();

            if (!CurrentEquals("PROCEDURE"))
            {
                ErrorHandler.Parser.Report(fileName, Current(), "expected", "Data Division data items and sections");
                ErrorHandler.Parser.PrettyError(fileName, Current());
                Continue();
            }

        }

        void WorkingStorage()
        {
            CurrentSection = Current().value;
            Expected("WORKING-STORAGE");
            Expected("SECTION");
            Expected(".");
            while (Current().type == TokenType.Numeric)
                Entries();
        }

        void LocalStorage()
        {
            CurrentSection = Current().value;
            Expected("LOCAL-STORAGE");
            Expected("SECTION");
            Expected(".");
            while (Current().type == TokenType.Numeric)
                Entries();
        }

        void LinkageSection()
        {
            CurrentSection = Current().value;
            Expected("LINKAGE");
            Expected("SECTION");
            Expected(".");
            while (Current().type == TokenType.Numeric)
                Entries();
        }

        void Entries()
        {
            if (CurrentEquals("77"))
                BaseEntry();

            if ((CurrentEquals("01") || CurrentEquals("1")) && !LookaheadEquals(2, "CONSTANT"))
                RecordEntry();

            if (LookaheadEquals(2, "CONSTANT"))
                ConstantEntry();
        }

        void RecordEntry()
        {
            BaseEntry();
            int OutInt;
            bool isNum = int.TryParse(Current().value, out OutInt);
            while (OutInt > 1 && OutInt < 50)
            {
                BaseEntry();
                isNum = int.TryParse(Current().value, out OutInt);
            }
        }

        void BaseEntry()
        {
            string dataType = string.Empty;
            int LevelNumber = int.Parse(Current().value);
            Number();

            string DataName = Current().value;
            Identifier();

            string DataItemHash = $"{SourceId}#{DataName}";
            if (!DataItemInformation.AddDataItem(DataItemHash, DataName, LevelNumber, Current()))
            {
                DataItemInfo originalItem = DataItemInformation.GetValue(DataItemHash);
                string duplicateDataItemError = $"""
                A data item with this name already exists in this program, data items in a program must have a unique name.
                The original {originalItem.Identifier} data item can be found at line {originalItem.Line}. 
                """;

                ErrorHandler.Parser.Report(fileName, Lookahead(-1), "general", duplicateDataItemError);
                ErrorHandler.Parser.PrettyError(fileName, Lookahead(-1));
            }

            DataItemInformation.AddSection(DataItemHash, CurrentSection);

            if (Current().context != TokenContext.IsClause && !CurrentEquals("."))
            {
                string notAClauseError = $"""
                Expected data division clauses or a separator period after this data item's identifier.
                Token found ("{Current().value}") was not a data division clause reserved word.
                """;

                ErrorHandler.Parser.Report(fileName, Current(), "general", notAClauseError);
                ErrorHandler.Parser.PrettyError(fileName, Current());
                Continue();
            }

            while (Current().context == TokenContext.IsClause)
            {
                if (CurrentEquals("IS") && !(LookaheadEquals(1, "EXTERNAL") || LookaheadEquals(1, "GLOBAL") || LookaheadEquals(1, "TYPEDEF")))
                {
                    string Externalerror = """
                    Missing clause or possible clause mismatch, in this context the "IS" word must be followed by the EXTERNAL, GLOBAL or TYPEDEF clauses only (IS TYPEDEF), or must be in the middle of the PICTURE clause (PIC IS ...) 
                    """;

                    ErrorHandler.Parser.Report(fileName, Current(), "general", Externalerror);
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                }

                if ((CurrentEquals("IS") && LookaheadEquals(1, "EXTERNAL")) || CurrentEquals("EXTERNAL"))
                {
                    Optional("IS");
                    Expected("EXTERNAL");
                    if (CurrentEquals("AS"))
                    {
                        string externalizedNameError = """
                        Missing externalized name, the "AS" word on the EXTERNAL clause must be followed by an alphanumeric or national literal
                        """;
                        Expected("AS");
                        DataItemInformation.IsExternal(DataItemHash, true, Current().value);
                        String(externalizedNameError, -1);
                    }

                    if (!CurrentEquals("AS"))
                        DataItemInformation.IsExternal(DataItemHash, true, DataName);
                }

                if (CurrentEquals("PIC") || CurrentEquals("PICTURE"))
                {
                    Choice(null, "PIC", "PICTURE");
                    Optional("IS");
                    dataType = Current().value switch
                    {
                        "S9" => "S9",
                        "9" => "9",
                        "X" => "X",
                        "A" => "A",
                        "N" => "N",
                        "1" => "1",
                        _ => "Error"
                    };

                    if (dataType == "Error")
                    {
                        string dataTypeError = """
                        Unrecognized type, PICTURE type must be S9, 9, X, A, N or 1. These are Signed Numeric, Unsigned Numeric, Alphanumeric, Alphabetic, National and Boolean respectively
                        """;

                        ErrorHandler.Parser.Report(fileName, Current(), "general", dataTypeError);
                        ErrorHandler.Parser.PrettyError(fileName, Current());
                    }

                    DataItemInformation.AddType(DataItemHash, dataType);
                    DataItemInformation.IsElementary(DataItemHash, true);
                    Choice(null, "S9", "9", "X", "A", "N", "1");

                    string DataLength = string.Empty;
                    Expected("(");
                    DataLength = Current().value;
                    Number();
                    Expected(")");
                    if (CurrentEquals("V9") && (dataType != "S9" && (dataType != "9")))
                    {
                        ErrorHandler.Parser.Report(fileName, Current(), " ", "V9 cannot be used with non-numeric types");
                        ErrorHandler.Parser.PrettyError(fileName, Current());
                    }

                    if (CurrentEquals("V9"))
                    {
                        Expected("V9");
                        Expected("(");
                        DataLength += $"V{Current().value}";
                        Number();
                        Expected(")");
                    }

                    DataItemInformation.AddPicture(DataItemHash, DataLength);
                }

                if (CurrentEquals("VALUE"))
                {
                    Expected("VALUE");

                    if (!Current().type.Equals(TokenType.String) && !Current().type.Equals(TokenType.Numeric))
                    {
                        string valueError = """
                        The only tokens allowed after a VALUE clause are type literals, like an Alphanumeric literal ("Hello, World!") or a Numeric literal (123.456).
                        """;

                        ErrorHandler.Parser.Report(fileName, Current(), "general", valueError);
                        ErrorHandler.Parser.PrettyError(fileName, Current());
                    }

                    if (Current().type.Equals(TokenType.String))
                    {
                        DataItemInformation.AddDefault(DataItemHash, Current().value);
                        String();
                    }

                    if (Current().type.Equals(TokenType.Numeric))
                    {
                        DataItemInformation.AddDefault(DataItemHash, Current().value);
                        Number();
                    }
                }

            }

            if (!DataItemInformation.GetValue(DataItemHash).IsElementary)
                DataItemInformation.IsGroup(DataItemHash, true);

            string separatorPeriodError = """
            Missing separator period at the end of this data item definition, each data item must end with a separator period
            """;
            Expected(".", separatorPeriodError, -1);
        }

        void ConstantEntry()
        {
            if (!CurrentEquals("01") && !CurrentEquals("1"))
            {
                string levelNumberError = """
                Invalid level number for this data item, CONSTANT data items must have a level number of 1 or 01
                """;

                ErrorHandler.Parser.Report(fileName, Current(), "general", levelNumberError);
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }

            int LevelNumber = int.Parse(Current().value);
            Number();

            string DataName = Current().value;
            Identifier();

            string DataItemHash = $"{SourceId}#{DataName}";
            if (!DataItemInformation.AddDataItem(DataItemHash, DataName, LevelNumber, Current()))
            {
                DataItemInfo originalItem = DataItemInformation.GetValue(DataItemHash);
                string duplicateDataItemError = $"""
                A data item with this name already exists in this program, data items in a program must have a unique name.
                The original {originalItem.Identifier} data item can be found at line {originalItem.Line}. 
                """;

                ErrorHandler.Parser.Report(fileName, Lookahead(-1), "general", duplicateDataItemError);
                ErrorHandler.Parser.PrettyError(fileName, Lookahead(-1));
            }
            DataItemInformation.IsConstant(DataItemHash, true);
            DataItemInformation.AddSection(DataItemHash, CurrentSection);

            Expected("CONSTANT");
            if (CurrentEquals("IS") || CurrentEquals("GLOBAL"))
            {
                Optional("IS");
                Expected("GLOBAL");
                DataItemInformation.IsGlobal(DataItemHash, true);
            }

            if (CurrentEquals("FROM"))
            {
                Expected("FROM");
                Identifier();
                Expected(".");
            }
            else
            {
                Optional("AS");
                switch (Current().type)
                {
                    case TokenType.String:
                        String();
                        break;

                    case TokenType.Numeric:
                        Number();
                        break;

                    case TokenType.FigurativeLiteral:
                        FigurativeLiteral();
                        break;
                }

                if (CurrentEquals("LENGTH"))
                {
                    Expected("LENGTH");
                    Optional("OF");
                    Identifier();
                }

                if (CurrentEquals("BYTE-LENGTH"))
                {
                    Expected("BYTE-LENGTH");
                    Optional("OF");
                    Identifier();
                }

                Expected(".");
            }
        }

        void PROCEDURE()
        {
            string headerPeriodError = """
            Missing separator period at the end of this PROCEDURE DIVISION header, every division header must end with a separator period
            """;

            Expected("PROCEDURE");
            Expected("DIVISION");
            if (SourceType.Equals("FUNCTION"))
            {
                Expected("RETURNING");
                ReturningDataName();
            }
            else if (!SourceType.Equals("FUNCTION") && CurrentEquals("RETURNING"))
            {
                Expected("RETURNING");
                ReturningDataName(); 
            }

            Expected(".", headerPeriodError, -1);
            Statement();

            if (CurrentEquals("IDENTIFICATION") || CurrentEquals("PROGRAM-ID") || CurrentEquals("FUNCTION-ID"))
            {
                string missingEndMarkerError = $"""
                Missing END {SourceType} marker. If another source unit is present after the end of the current source unit, the current unit must contain an END marker.
                """;

                string missingEndFunctionMarkerError = $"""
                Missing END FUNCTION marker. User-defined functions must always end with an END FUNCTION marker.
                """;

                string errorMessageChoice = SourceType.Equals("FUNCTION") ? missingEndFunctionMarkerError : missingEndMarkerError;

                ErrorHandler.Parser.Report(fileName, Lookahead(-1), "general", errorMessageChoice);
                ErrorHandler.Parser.PrettyError(fileName, Lookahead(-1));
                return;
            }

            if (SourceType.Equals("PROGRAM") && CurrentEquals("END") && LookaheadEquals(1, "PROGRAM"))
            {
                string endProgramPeriodError = """
                Missing separator period at the end of this END PROGRAM definition
                """;

                Expected("END");
                Expected("PROGRAM");
                Identifier();
                Expected(".", endProgramPeriodError, -1);
            }

            if (SourceType.Equals("FUNCTION"))
            {
                string endFunctionPeriodError = """
                Missing separator period at the end of this END FUNCTION definition
                """;

                Expected("END");
                Expected("FUNCTION");
                Identifier();
                Expected(".", endFunctionPeriodError, -1);
            }
        }

        void ReturningDataName()
        {
            if (Current().type != TokenType.Identifier)
            {
                string missingDataItemError = $"""
                Missing returning data item after this RETURNING definition.
                """;

                ErrorHandler.Parser.Report(fileName, Lookahead(-1), "general", missingDataItemError);
                ErrorHandler.Parser.PrettyError(fileName, Lookahead(-1));
                return;
            }

            string DataName = Current().value;
            Identifier();

            string DataItemHash = $"{SourceId}#{DataName}";
            if (!DataItemInformation.ValueExists(DataItemHash))
            {
                string undefinedDataItemError = $"""
                No data item found with this name in this source unit's data division. 
                Please define a new returning data item in this unit's linkage section.
                """;

                ErrorHandler.Parser.Report(fileName, Lookahead(-1), "general", undefinedDataItemError);
                ErrorHandler.Parser.PrettyError(fileName, Lookahead(-1));
                return;
            }
        }

        void Statement(bool isNested = false)
        {
            while (Current().context == TokenContext.IsStatement)
            {
                if (CurrentEquals("ACCEPT"))
                    ACCEPT();

                if (CurrentEquals("ADD"))
                    ADD();

                if (CurrentEquals("ALLOCATE"))
                    ALLOCATE();

                if (CurrentEquals("CALL"))
                    CALL();

                if (CurrentEquals("CANCEL"))
                    CANCEL();

                if (CurrentEquals("CLOSE"))
                    CLOSE();

                if (CurrentEquals("COMMIT"))
                    COMMIT();

                if (CurrentEquals("CONTINUE"))
                    CONTINUE();

                if (CurrentEquals("COMPUTE"))
                    COMPUTE();

                if (CurrentEquals("DISPLAY"))
                    DISPLAY();

                if (CurrentEquals("DIVIDE"))
                    DIVIDE();

                if (CurrentEquals("DELETE"))
                    DELETE();

                if (CurrentEquals("IF"))
                    IF();

                if (CurrentEquals("INITIATE"))
                    INITIATE();

                if (CurrentEquals("MULTIPLY"))
                    MULTIPLY();

                if (CurrentEquals("MOVE"))
                    MOVE();

                if (CurrentEquals("EXIT"))
                    EXIT();

                if (CurrentEquals("FREE"))
                    FREE();

                if (CurrentEquals("GENERATE"))
                    GENERATE();

                if (CurrentEquals("GO"))
                    GO();

                if (CurrentEquals("GOBACK"))
                    GOBACK();

                if (CurrentEquals("SUBTRACT"))
                    SUBTRACT();

                if (CurrentEquals("RELEASE"))
                    RELEASE();

                if (CurrentEquals("RAISE"))
                    RAISE();

                if (CurrentEquals("RESUME"))
                    RESUME();

                if (CurrentEquals("RETURN"))
                    RETURN();

                if (CurrentEquals("REWRITE"))
                    REWRITE();

                if (CurrentEquals("ROLLBACK"))
                    ROLLBACK();

                if (CurrentEquals("STOP"))
                    STOP();

                if (CurrentEquals("SUPPRESS"))
                    SUPPRESS();

                if (CurrentEquals("TERMINATE"))
                    TERMINATE();

                if (CurrentEquals("UNLOCK"))
                    UNLOCK();

                if (CurrentEquals("VALIDATE"))
                    VALIDATE();

                ScopeTerminator(isNested);
                Statement(isNested);
            }
        }

        void ScopeTerminator(bool isNested)
        {
            if (isNested)
                return;

            Expected(".", "expected", 0);
        }

        // Statement parsing section:
        void DISPLAY()
        {
            Expected("DISPLAY");
            switch (Current().type)
            {
                case TokenType.Identifier:
                    Identifier();
                    break;
                case TokenType.Numeric:
                    Number();
                    break;
                case TokenType.String:
                    String();
                    break;
                default:
                    ErrorHandler.Parser.Report(fileName, Current(), "expected", "identifier or literal");
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                    break;
            }

            while (Current().type == TokenType.Identifier
                || Current().type == TokenType.Numeric
                || Current().type == TokenType.String
                )
            {
                if (Current().type == TokenType.Identifier)
                    Identifier();

                if (Current().type == TokenType.Numeric)
                    Number();

                if (Current().type == TokenType.String)
                    String();
            }

            if (CurrentEquals("UPON"))
            {
                Expected("UPON");
                Choice(TokenType.Device, "STANDARD-OUTPUT", "STANDARD-ERROR");
            }

            if (CurrentEquals("WITH") || CurrentEquals("NO"))
            {
                Optional("WITH");
                Expected("NO");
                Expected("ADVANCING");
            }

            Optional("END-DISPLAY");
        }

        void ACCEPT()
        {
            Expected("ACCEPT");
            Identifier();
            if (CurrentEquals("FROM"))
            {
                Expected("FROM");
                switch (Current().value)
                {
                    case "STANDARD-INPUT":
                    case "COMMAND-LINE":
                        Choice(TokenType.Device, "STANDARD-INPUT", "COMMAND-LINE");
                        break;

                    case "DATE":
                        Expected("DATE");
                        Optional("YYYYMMDD");
                        break;

                    case "DAY":
                        Expected("DAY");
                        Optional("YYYYDDD");
                        break;

                    case "DAY-OF-WEEK":
                        Expected("DAY-OF-WEEK");
                        break;

                    case "TIME":
                        Expected("TIME");
                        break;
                }
            }

            Optional("END-ACCEPT");
        }

        void ALLOCATE()
        {
            Expected("ALLOCATE");
            if (Current().type == TokenType.Identifier && !LookaheadEquals(1, "CHARACTERS") && Lookahead(1).type != TokenType.Symbol)
                Identifier();

            if (Current().type == TokenType.Identifier || Current().type == TokenType.Numeric)
            {
                Arithmetic();
                Expected("CHARACTERS");
            }

            if (CurrentEquals("INITIALIZED"))
                Expected("INITIALIZED");

            if (CurrentEquals("RETURNING"))
            {
                Expected("RETURNING");
                Identifier();
            }
        }

        void COMPUTE()
        {
            bool isConditional = false;

            Expected("COMPUTE");
            if (Current().type != TokenType.Identifier)
            {
                ErrorHandler.Parser.Report(fileName, Current(), "expected", "identifier");
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }

            while (Current().type == TokenType.Identifier)
            {
                Identifier();
            }

            Expected("=");
            if (Current().type != TokenType.Identifier && Current().type != TokenType.Numeric && Current().type != TokenType.Symbol)
            {
                ErrorHandler.Parser.Report(fileName, Current(), "expected", "identifier, numeric literal or valid arithmetic symbol");
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }

            Arithmetic();

            if (CurrentEquals("."))
            {
                Expected(".");
                return;
            }

            SizeError(ref isConditional);

            if (isConditional)
                Expected("END-COMPUTE");
        }

        void CALL()
        {
            Expected("CALL");
            String();
            Optional("END-CALL");
        }

        void CONTINUE()
        {
            Expected("CONTINUE");
            if (CurrentEquals("AFTER"))
            {
                Expected("AFTER");
                Arithmetic();
                Expected("SECONDS");
            }
        }

        void ADD()
        {
            bool isConditional = false;

            Expected("ADD");
            if (Current().type != TokenType.Identifier && Current().type != TokenType.Numeric)
                ErrorHandler.Parser.Report(fileName, Current(), "expected", "identifier or numeric literal");

            while (Current().type == TokenType.Identifier
                || Current().type == TokenType.Numeric
            )
            {
                if (Current().type == TokenType.Identifier)
                    Identifier();

                if (Current().type == TokenType.Numeric)
                    Number();
            }

            if (CurrentEquals("TO") && LookaheadEquals(2, "GIVING"))
            {
                Optional("TO");
                switch (Current().type)
                {
                    case TokenType.Identifier:
                        Identifier();
                        break;

                    case TokenType.Numeric:
                        Number();
                        break;

                    default:
                        ErrorHandler.Parser.Report(fileName, Current(), "expected", "identifier or numeric literal");
                        ErrorHandler.Parser.PrettyError(fileName, Current());
                        break;
                }

                Expected("GIVING");
                if (Current().type != TokenType.Identifier)
                {
                    ErrorHandler.Parser.Report(fileName, Current(), "expected", "identifier");
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                }

                while (Current().type == TokenType.Identifier)
                    Identifier();
            }
            else if (CurrentEquals("GIVING"))
            {
                Expected("GIVING");
                if (Current().type != TokenType.Identifier)
                {
                    ErrorHandler.Parser.Report(fileName, Current(), "expected", "identifier");
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                }

                while (Current().type == TokenType.Identifier)
                    Identifier();
            }
            else if (CurrentEquals("TO"))
            {
                Expected("TO");
                if (Current().type != TokenType.Identifier)
                {
                    ErrorHandler.Parser.Report(fileName, Current(), "expected", "identifier");
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                }

                while (Current().type == TokenType.Identifier)
                    Identifier();
            }
            else
            {
                ErrorHandler.Parser.Report(fileName, Current(), "expected", "TO or GIVING");
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }

            SizeError(ref isConditional);

            if (isConditional)
                Expected("END-ADD");
        }

        void SUBTRACT()
        {
            bool isConditional = false;

            Expected("SUBTRACT");
            if (Current().type != TokenType.Identifier && Current().type != TokenType.Numeric)
            {
                ErrorHandler.Parser.Report(fileName, Current(), "expected", "identifier or numeric literal");
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }

            while (Current().type == TokenType.Identifier
                || Current().type == TokenType.Numeric
            )
            {
                if (Current().type == TokenType.Identifier)
                    Identifier();

                if (Current().type == TokenType.Numeric)
                    Number();
            }

            if (CurrentEquals("FROM") && LookaheadEquals(2, "GIVING"))
            {
                Optional("FROM");
                switch (Current().type)
                {
                    case TokenType.Identifier:
                        Identifier();
                        break;

                    case TokenType.Numeric:
                        Number();
                        break;

                    default:
                        ErrorHandler.Parser.Report(fileName, Current(), "expected", "identifier or numeric literal");
                        ErrorHandler.Parser.PrettyError(fileName, Current());
                        break;
                }

                Expected("GIVING");
                if (Current().type != TokenType.Identifier)
                {
                    ErrorHandler.Parser.Report(fileName, Current(), "expected", "identifier");
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                }

                while (Current().type == TokenType.Identifier)
                    Identifier();
            }
            else if (CurrentEquals("FROM"))
            {
                Expected("FROM");
                if (Current().type != TokenType.Identifier)
                {
                    ErrorHandler.Parser.Report(fileName, Current(), "expected", "identifier");
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                }

                while (Current().type == TokenType.Identifier)
                    Identifier();
            }
            else
            {
                ErrorHandler.Parser.Report(fileName, Current(), "expected", "FROM");
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }

            SizeError(ref isConditional);

            if (isConditional)
                Expected("END-SUBTRACT");
        }

        void IF()
        {
            Expected("IF");
            Condition("THEN");
            Optional("THEN");
            if (CurrentEquals("NEXT") && LookaheadEquals(1, "SENTENCE"))
            {
                string archaicFeatureError = """
                Unsupported phrase: NEXT SENTENCE is an archaic feature. This phrase can be confusing and is a common source of errors.
                The CONTINUE statement can be used to accomplish the same functionality while being much clearer and less prone to error
                """;

                ErrorHandler.Parser.Report(fileName, Current(), "general", archaicFeatureError);
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }
            Statement(true);
            if (CurrentEquals("ELSE"))
            {
                Expected("ELSE");
                Statement(true);
            }

            Expected("END-IF");
        }

        void INITIATE()
        {
            Expected("INITIATE");
            if (Current().type != TokenType.Identifier)
            {
                string notIdentifierError = """
                The INITIATE statement must only contain report entry identifiers defined in the report section.
                """;

                ErrorHandler.Parser.Report(fileName, Current(), "general", notIdentifierError);
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }
            Identifier();
            while (Current().type == TokenType.Identifier)
                Identifier();

            if (!CurrentEquals("."))
            {
                string notIdentifierError = """
                The INITIATE statement must only contain report entry identifiers defined in the report section.
                """;

                ErrorHandler.Parser.Report(fileName, Current(), "general", notIdentifierError);
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }
        }

        void MULTIPLY()
        {
            bool isConditional = false;

            Expected("MULTIPLY");
            switch (Current().type)
            {
                case TokenType.Identifier:
                    Identifier();
                    break;

                case TokenType.Numeric:
                    Number();
                    break;

                default:
                    ErrorHandler.Parser.Report(fileName, Current(), "expected", "identifier or numeric literal");
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                    break;
            }

            if (CurrentEquals("BY") && LookaheadEquals(2, "GIVING"))
            {
                Optional("BY");
                switch (Current().type)
                {
                    case TokenType.Identifier:
                        Identifier();
                        break;

                    case TokenType.Numeric:
                        Number();
                        break;

                    default:
                        ErrorHandler.Parser.Report(fileName, Current(), "expected", "identifier or numeric literal");
                        ErrorHandler.Parser.PrettyError(fileName, Current());
                        break;
                }

                Expected("GIVING");
                if (Current().type != TokenType.Identifier)
                {
                    ErrorHandler.Parser.Report(fileName, Current(), "expected", "identifier");
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                }

                while (Current().type == TokenType.Identifier)
                    Identifier();
            }
            else if (CurrentEquals("BY"))
            {
                Expected("BY");
                if (Current().type != TokenType.Identifier)
                {
                    ErrorHandler.Parser.Report(fileName, Current(), "expected", "identifier");
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                }

                while (Current().type == TokenType.Identifier)
                    Identifier();
            }
            else
            {
                ErrorHandler.Parser.Report(fileName, Current(), "expected", "BY");
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }

            SizeError(ref isConditional);

            if (isConditional)
                Expected("END-MULTIPLY");
        }

        void MOVE()
        {
            Expected("MOVE");
            if (CurrentEquals("CORRESPONDING") || CurrentEquals("CORR"))
            {
                Expected(Current().value);
                Identifier();
                Expected("TO");
                Identifier();
                return;
            }

            if (NotIdentifierOrLiteral())
            {
                string notIdentifierOrLiteralError = """
                The MOVE statement must only contain a single data item identifier, datatype literal or an intrisic function which returns a data item before the "TO" reserved word.
                """;

                ErrorHandler.Parser.Report(fileName, Current(), "general", notIdentifierOrLiteralError);
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }

            if (Current().type == TokenType.Identifier)
                Identifier();

            else if (Current().type == TokenType.Numeric)
                Number();

            else if (Current().type == TokenType.String)
                String();

            Expected("TO");
            if (Current().type != TokenType.Identifier)
            {
                string notIdentifierError = """
                The MOVE statement must only contain data item identifiers after the "TO" reserved word.
                """;

                ErrorHandler.Parser.Report(fileName, Current(), "general", notIdentifierError);
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }

            while (Current().type == TokenType.Identifier)
                Identifier();

            if (!CurrentEquals("."))
            {
                string notIdentifierError = """
                The MOVE statement must only contain data item identifiers after the "TO" reserved word.
                """;

                ErrorHandler.Parser.Report(fileName, Current(), "general", notIdentifierError);
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }
        }

        void DIVIDE()
        {
            bool isConditional = false;

            Expected("DIVIDE");
            switch (Current().type)
            {
                case TokenType.Identifier:
                    Identifier();
                    break;

                case TokenType.Numeric:
                    Number();
                    break;

                default:
                    ErrorHandler.Parser.Report(fileName, Current(), "expected", "identifier or numeric literal");
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                    break;
            }

            if ((CurrentEquals("BY") || CurrentEquals("INTO")) && LookaheadEquals(2, "GIVING") && !LookaheadEquals(4, "REMAINDER"))
            {
                Choice(null, "BY", "INTO");
                switch (Current().type)
                {
                    case TokenType.Identifier:
                        Identifier();
                        break;

                    case TokenType.Numeric:
                        Number();
                        break;

                    default:
                        ErrorHandler.Parser.Report(fileName, Current(), "expected", "identifier or numeric literal");
                        ErrorHandler.Parser.PrettyError(fileName, Current());
                        break;
                }

                Expected("GIVING");
                if (Current().type != TokenType.Identifier)
                {
                    ErrorHandler.Parser.Report(fileName, Current(), "expected", "identifier");
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                }

                while (Current().type == TokenType.Identifier)
                    Identifier();
            }
            else if ((CurrentEquals("BY") || CurrentEquals("INTO")) && LookaheadEquals(2, "GIVING") && LookaheadEquals(4, "REMAINDER"))
            {
                Choice(null, "BY", "INTO");
                switch (Current().type)
                {
                    case TokenType.Identifier:
                        Identifier();
                        break;

                    case TokenType.Numeric:
                        Number();
                        break;

                    default:
                        ErrorHandler.Parser.Report(fileName, Current(), "expected", "identifier or numeric literal");
                        ErrorHandler.Parser.PrettyError(fileName, Current());
                        break;
                }

                Expected("GIVING");
                Identifier();
                Expected("REMAINDER");
                Identifier();
            }
            else if (CurrentEquals("INTO"))
            {
                Expected("INTO");
                if (Current().type != TokenType.Identifier)
                {
                    ErrorHandler.Parser.Report(fileName, Current(), "expected", "identifier");
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                }

                while (Current().type == TokenType.Identifier)
                    Identifier();
            }
            else
            {
                ErrorHandler.Parser.Report(fileName, Current(), "expected", "BY or INTO");
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }

            SizeError(ref isConditional);

            if (isConditional)
                Expected("END-MULTIPLY");
        }

        void DELETE()
        {
            bool isConditional = false;
            bool isFile = false;

            Expected("DELETE");
            if (CurrentEquals("FILE"))
            {
                isFile = true;
                Expected("FILE");
                Optional("OVERRIDE");
                Identifier();
                while (Current().type == TokenType.Identifier)
                    Identifier();
            }
            else if (Current().type == TokenType.Identifier)
            {
                Identifier();
                Expected("RECORD");
            }

            if (CurrentEquals("RETRY"))
                RetryPhrase();
            
            if (!isFile)
                InvalidKey(ref isConditional);

            if (isFile)
                OnException(ref isConditional);

            if (isConditional)
                Expected("END-DELETE");
        }

        void EXIT()
        {
            Expected("EXIT");
            if (CurrentEquals("PERFORM"))
            {
                Expected("PERFORM");
                Optional("CYCLE");
            }
            else if (CurrentEquals("PARAGRAPH"))
                Expected("PARAGRAPH");

            else if (CurrentEquals("SECTION"))
                Expected("SECTION");

            else if (CurrentEquals("PROGRAM"))
            {
                Expected("PROGRAM");
                if (CurrentEquals("RAISING"))
                {
                    Expected("RAISING");
                    if (CurrentEquals("EXCEPTION"))
                    {
                        Expected("EXCEPTION");
                        Identifier();
                    }
                    else if (CurrentEquals("LAST"))
                    {
                        Expected("LAST");
                        Optional("EXCEPTION");
                    }
                    else
                        Identifier();
                }
            }
        }

        void FREE()
        {
            Expected("FREE");
            Identifier();
            while (Current().type == TokenType.Identifier)
                Identifier();

            if (!CurrentEquals("."))
            {
                string notIdentifierError = """
                    The FREE statement must only contain based data item identifiers.
                    """;

                ErrorHandler.Parser.Report(fileName, Current(), "general", notIdentifierError);
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }

        }

        void GENERATE()
        {
            Expected("GENERATE");
            Identifier();
        }

        void GO()
        {
            Expected("GO");
            Optional("TO");
            Identifier();
            if (CurrentEquals("DEPENDING") || Current().type == TokenType.Identifier)
            {
                while (Current().type == TokenType.Identifier)
                    Identifier();

                Expected("DEPENDING");
                Optional("ON");
                Identifier();
            }
        }

        void GOBACK()
        {
            Expected("GOBACK");
            RaisingStatus();
        }

        void COMMIT()
        {
            Expected("COMMIT");
        }

        void CLOSE()
        {
            Expected("CLOSE");
            if (Current().type == TokenType.Identifier)
            {
                Identifier();
                if (CurrentEquals("REEL") || CurrentEquals("UNIT"))
                {
                    Expected(Current().value);

                    if (CurrentEquals("FOR") || CurrentEquals("REMOVAL"))
                    {
                        Optional("FOR");
                        Expected("REMOVAL");
                    }
                }
                else if (CurrentEquals("WITH") || CurrentEquals("NO"))
                {
                    Optional("WITH");
                    Expected("NO");
                    Expected("REWIND");
                }
            }
            else
            {
                string notProgramNameError = """
                The CLOSE statement only accepts file connector names. 
                NOTE: This statement must not specify more than one file connector when inside of an exception-checking phrase in a PERFORM statement.
                """;

                ErrorHandler.Parser.Report(fileName, Current(), "general", notProgramNameError);
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }

            while (Current().type == TokenType.Identifier)
            {
                Identifier();
                if (CurrentEquals("REEL") || CurrentEquals("UNIT"))
                {
                    Expected(Current().value);

                    if (CurrentEquals("FOR") || CurrentEquals("REMOVAL"))
                    {
                        Optional("FOR");
                        Expected("REMOVAL");
                    }
                }
                else if (CurrentEquals("WITH") || CurrentEquals("NO"))
                {
                    Optional("WITH");
                    Expected("NO");
                    Expected("REWIND");
                }
            }

            if (!CurrentEquals("."))
            {
                string notProgramNameError = """
                The CLOSE statement only accepts file connector names. 
                NOTE: This statement must not specify more than one file connector when inside of an exception-checking phrase in a PERFORM statement.
                """;

                ErrorHandler.Parser.Report(fileName, Current(), "general", notProgramNameError);
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }
        }

        void CANCEL()
        {
            Expected("CANCEL");
            if (Current().type == TokenType.Identifier)
                Identifier();

            else if (Current().type == TokenType.String)
                String();

            else
            {
                string notProgramNameError = """
                The CANCEL statement only accepts Alphanumeric or National literals and data items, or a program prototype name specified in the REPOSITORY paragraph.
                """;

                ErrorHandler.Parser.Report(fileName, Current(), "general", notProgramNameError);
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }

            while (Current().type == TokenType.Identifier || Current().type == TokenType.String)
            {
                if (Current().type == TokenType.Identifier)
                    Identifier();

                if (Current().type == TokenType.String)
                    String();
            }

            if (!CurrentEquals("."))
            {
                string notProgramNameError = """
                The CANCEL statement only accepts Alphanumeric or National literals and data items, or a program prototype name specified in the REPOSITORY paragraph.
                """;

                ErrorHandler.Parser.Report(fileName, Current(), "general", notProgramNameError);
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }
        }

        void RAISE()
        {
            Expected("RAISE");
            if (CurrentEquals("EXCEPTION"))
            {
                Expected("EXCEPTION");
                Identifier();
            }
            else
                Identifier();
        }

        void RELEASE()
        {
            Expected("RELEASE");
            Identifier();

            if (CurrentEquals("FROM"))
            {
                Expected("FROM");
                if (Current().type == TokenType.String)
                    String();

                else if (Current().type == TokenType.Numeric)
                    Number();

                else
                    Identifier();
            }
        }

        void RETURN()
        {
            bool isConditional = false;

            Expected("RETURN");
            Identifier();
            Expected("RECORD");
            if (CurrentEquals("INTO"))
            {
                Expected("INTO");
                Identifier();
            }

            AtEnd(ref isConditional);

            if (isConditional)
                Expected("END-RETURN");
        }

        void REWRITE()
        {
            bool isConditional = false;
            bool isFile = false;

            Expected("REWRITE");
            if (CurrentEquals("FILE"))
            {
                isFile = true;
                Expected("FILE");
                Identifier();
            }
            else
                Identifier();

            Expected("RECORD");
            if (CurrentEquals("FROM") || isFile)
            {
                Expected("FROM");

                if (Current().type == TokenType.Identifier)
                    Identifier();

                else if (Current().type == TokenType.Numeric)
                    Number();

                else
                    String();
            }

            RetryPhrase();
            if (CurrentEquals("WITH") || CurrentEquals("LOCK") || CurrentEquals("NO"))
            {
                Optional("WITH");
                if (CurrentEquals("LOCK"))
                {
                    Expected("LOCK");
                }
                else
                {
                    Expected("NO");
                    Expected("LOCK");
                }
            }

            InvalidKey(ref isConditional);

            if (isConditional)
                Expected("END-REWRITE");
        }

        void RESUME()
        {
            Expected("RESUME");
            Optional("AT");
            if (CurrentEquals("NEXT"))
            {
                Expected("NEXT");
                Expected("STATEMENT");
            }
            else
            {
                Identifier();
            }
        }

        void ROLLBACK()
        {
            Expected("ROLLBACK");
        }

        void STOP()
        {
            Expected("STOP");
            Expected("RUN");
            if (CurrentEquals("WITH") || CurrentEquals("NORMAL") || CurrentEquals("ERROR"))
            {
                Optional("WITH");
                Choice(null, "NORMAL", "ERROR");
                Optional("STATUS");
                switch (Current().type)
                {
                    case TokenType.Identifier:
                        Identifier();
                        break;
                    case TokenType.Numeric:
                        Number();
                        break;
                    case TokenType.String:
                        String();
                        break;
                }
            }
        }

        void SUPPRESS()
        {
            Expected("SUPPRESS");
            Optional("PRINTING");
        }

        void TERMINATE()
        {
            Expected("TERMINATE");
            if (Current().type != TokenType.Identifier)
            {
                string notIdentifierError = """
                The TERMINATE statement must only contain report entry identifiers defined in the report section.
                """;

                ErrorHandler.Parser.Report(fileName, Current(), "general", notIdentifierError);
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }
            Identifier();
            while (Current().type == TokenType.Identifier)
                Identifier();

            if (!CurrentEquals("."))
            {
                string notIdentifierError = """
                The TERMINATE statement must only contain report entry identifiers defined in the report section.
                """;

                ErrorHandler.Parser.Report(fileName, Current(), "general", notIdentifierError);
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }
        }

        void UNLOCK()
        {
            Expected("UNLOCK");
            Identifier();
            Choice(null, "RECORD", "RECORDS");
        }

        void VALIDATE()
        {
            Expected("VALIDATE");
            if (Current().type != TokenType.Identifier)
            {
                string notIdentifierError = """
                The VALIDATE statement must only contain data item identifiers.
                """;

                ErrorHandler.Parser.Report(fileName, Current(), "general", notIdentifierError);
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }
            Identifier();
            while (Current().type == TokenType.Identifier)
                Identifier();

            if (!CurrentEquals("."))
            {
                string notIdentifierError = """
                The VALIDATE statement must only contain data item identifiers.
                """;

                ErrorHandler.Parser.Report(fileName, Current(), "general", notIdentifierError);
                ErrorHandler.Parser.PrettyError(fileName, Current());
            }
        }

        // Parser helper methods.
        Token Lookahead(int amount)
        {
            return tokenList[index + amount];
        }

        bool LookaheadEquals(int lookahead, string stringToCompare)
        {
            return Lookahead(lookahead).value.Equals(stringToCompare);
        }

        Token Current()
        {
            return tokenList[index];
        }

        bool CurrentEquals(string stringToCompare)
        {
            return Current().value.Equals(stringToCompare);
        }

        void Continue()
        {
            index += 1;
            return;
        }

        void Choice(TokenType? type, params string[] choices)
        {
            Token current = Current();
            foreach (string choice in choices)
            {
                if (current.value.Equals(choice))
                {
                    if (type != null)
                        current.type = type;
                    analyzed.Add(current);
                    Continue();
                    return;
                }
            }

            ErrorHandler.Parser.Report(fileName, Current(), "choice", choices);
            ErrorHandler.Parser.PrettyError(fileName, Current());
            Continue();
            return;
        }

        void Optional(string optional, string scope = "")
        {
            Token current = Current();
            if (!current.value.Equals(optional))
                return;

            analyzed.Add(current);
            Continue();
            return;
        }

        void Expected(string expected, string custom = "expected", int position = 0, string scope = "")
        {
            string errorMessage = expected;
            string errorType = "expected";
            Token token = Current();
            if (!custom.Equals("expected"))
            {
                errorMessage = custom;
                errorType = "general";
            }

            if (position != 0)
                token = Lookahead(position);

            Token current = Current();
            if (!current.value.Equals(expected))
            {
                ErrorHandler.Parser.Report(fileName, token, errorType, errorMessage);
                ErrorHandler.Parser.PrettyError(fileName, token);
                Continue();
                return;
            }

            analyzed.Add(current);
            Continue();
            return;
        }

        void RetryPhrase()
        {
            bool hasFor = false;

            Expected("RETRY");
            if (CurrentEquals("FOREVER"))
            {
                Expected("FOREVER");
                return;
            }

            if (CurrentEquals("FOR"))
            {
                Optional("FOR");
                hasFor = true;
            }

            Arithmetic();
            if (CurrentEquals("SECONDS") || hasFor)
                Expected("SECONDS");

            else
                Expected("TIMES");
        }

        void InvalidKey(ref bool isConditional, bool invalidKeyExists = false, bool notInvalidKeyExists = false)
        {
            if (CurrentEquals("INVALID"))
            {
                if (invalidKeyExists)
                {
                    string onErrorExistsError = """
                    INVALID KEY can only be specified once in this statement. 
                    The same applies to the NOT INVALID KEY.
                    """;
                    ErrorHandler.Parser.Report(fileName, Current(), "general", onErrorExistsError);
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                }
                isConditional = true;
                invalidKeyExists = true;
                Expected("INVALID");
                Optional("KEY");
                Statement(true);
                InvalidKey(ref isConditional, invalidKeyExists, notInvalidKeyExists);

            }

            if (CurrentEquals("NOT"))
            {
                if (notInvalidKeyExists)
                {
                    string notOnErrorExistsError = """
                    NOT INVALID KEY can only be specified once in this statement. 
                    The same applies to the INVALID KEY.
                    """;
                    ErrorHandler.Parser.Report(fileName, Current(), "general", notOnErrorExistsError);
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                }
                isConditional = true;
                notInvalidKeyExists = true;
                Expected("NOT");
                Expected("INVALID");
                Optional("KEY");
                Statement(true);
                InvalidKey(ref isConditional, invalidKeyExists, notInvalidKeyExists);
            }
        }

        void OnException(ref bool isConditional, bool onExceptionExists = false, bool notOnExceptionExists = false)
        {
            if (CurrentEquals("ON") || CurrentEquals("EXCEPTION"))
            {
                if (onExceptionExists)
                {
                    string onExceptionExistsError = """
                    ON EXCEPTION can only be specified once in this statement. 
                    The same applies to the NOT ON EXCEPTION.
                    """;
                    ErrorHandler.Parser.Report(fileName, Current(), "general", onExceptionExistsError);
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                }
                isConditional = true;
                onExceptionExists = true;
                Optional("ON");
                Expected("EXCEPTION");
                Statement(true);
                OnException(ref isConditional, onExceptionExists, notOnExceptionExists);

            }

            if (CurrentEquals("NOT"))
            {
                if (notOnExceptionExists)
                {
                    string notOnExceptionExistsError = """
                    NOT ON EXCEPTION can only be specified once in this statement. 
                    The same applies to the ON EXCEPTION.
                    """;
                    ErrorHandler.Parser.Report(fileName, Current(), "general", notOnExceptionExistsError);
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                }
                isConditional = true;
                notOnExceptionExists = true;
                Expected("NOT");
                Optional("ON");
                Expected("EXCEPTION");
                Statement(true);
                OnException(ref isConditional, onExceptionExists, notOnExceptionExists);
            }
        }

        void RaisingStatus(bool raisingExists = false, bool statusExists = false)
        {
            if (CurrentEquals("RAISING"))
            {
                if (raisingExists)
                {
                    string onExceptionExistsError = """
                    RAISING can only be specified once in this statement. 
                    The same applies to the WITH NORMAL/ERROR STATUS.
                    """;
                    ErrorHandler.Parser.Report(fileName, Current(), "general", onExceptionExistsError);
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                }

                Expected("RAISING");
                if (CurrentEquals("EXCEPTION"))
                {
                    Expected("EXCEPTION");
                    Identifier();
                }
                else if (CurrentEquals("LAST"))
                {
                    Expected("LAST");
                    Optional("EXCEPTION");
                }
                else
                    Identifier();
                
                raisingExists = true;
                RaisingStatus(raisingExists, statusExists);

            }

            if (CurrentEquals("WITH") || CurrentEquals("NORMAL") || CurrentEquals("ERROR"))
            {
                if (statusExists)
                {
                    string notOnExceptionExistsError = """
                    WITH NORMAL/ERROR STATUS can only be specified once in this statement. 
                    The same applies to the RAISING.
                    """;
                    ErrorHandler.Parser.Report(fileName, Current(), "general", notOnExceptionExistsError);
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                }

                Optional("WITH");
                Choice(null, "NORMAL", "ERROR");
                Optional("STATUS");
                switch (Current().type)
                {
                    case TokenType.Identifier:
                        Identifier();
                        break;
                    case TokenType.Numeric:
                        Number();
                        break;
                    case TokenType.String:
                        String();
                        break;
                }

                statusExists = true;
                RaisingStatus(raisingExists, statusExists);
            }
        }

        void AtEnd(ref bool isConditional, bool atEndExists = false, bool notAtEndExists = false)
        {
            if (CurrentEquals("AT") || CurrentEquals("END"))
            {
                if (atEndExists)
                {
                    string onExceptionExistsError = """
                    AT END can only be specified once in this statement. 
                    The same applies to the NOT AT END.
                    """;
                    ErrorHandler.Parser.Report(fileName, Current(), "general", onExceptionExistsError);
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                }
                isConditional = true;
                atEndExists = true;
                Optional("AT");
                Expected("END");
                Statement(true);
                AtEnd(ref isConditional, atEndExists, notAtEndExists);

            }

            if (CurrentEquals("NOT"))
            {
                if (notAtEndExists)
                {
                    string notOnExceptionExistsError = """
                    NOT AT END can only be specified once in this statement. 
                    The same applies to the AT END.
                    """;
                    ErrorHandler.Parser.Report(fileName, Current(), "general", notOnExceptionExistsError);
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                }
                isConditional = true;
                notAtEndExists = true;
                Expected("NOT");
                Optional("AT");
                Expected("END");
                Statement(true);
                AtEnd(ref isConditional, atEndExists, notAtEndExists);
            }
        }

        void SizeError(ref bool isConditional, bool onErrorExists = false, bool notOnErrorExists = false)
        {
            if (CurrentEquals("ON") || CurrentEquals("SIZE"))
            {
                if (onErrorExists)
                {
                    string onErrorExistsError = """
                    ON SIZE ERROR can only be specified once in this statement. 
                    The same applies to NOT ON SIZE ERROR.
                    """;
                    ErrorHandler.Parser.Report(fileName, Current(), "general", onErrorExistsError);
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                }
                isConditional = true;
                onErrorExists = true;
                Optional("ON");
                Expected("SIZE");
                Expected("ERROR");
                Statement(true);
                SizeError(ref isConditional, onErrorExists, notOnErrorExists);

            }

            if (CurrentEquals("NOT"))
            {
                if (notOnErrorExists)
                {
                    string notOnErrorExistsError = """
                    NOT ON SIZE ERROR can only be specified once in this statement. 
                    The same applies to ON SIZE ERROR.
                    """;
                    ErrorHandler.Parser.Report(fileName, Current(), "general", notOnErrorExistsError);
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                }
                isConditional = true;
                notOnErrorExists = true;
                Expected("NOT");
                Optional("ON");
                Expected("SIZE");
                Expected("ERROR");
                Statement(true);
                SizeError(ref isConditional, onErrorExists, notOnErrorExists);
            }
        }

        void Arithmetic()
        {
            bool isArithmeticSymbol(Token current) => current.value switch
            {
                "+" => true,
                "-" => true,
                "*" => true,
                "/" => true,
                "**" => true,
                "(" => true,
                ")" => true,
                _ => false
            };

            while (Current().type == TokenType.Identifier || Current().type == TokenType.Numeric || Current().type == TokenType.Symbol)
            {
                if (Current().type == TokenType.Identifier)
                    Identifier();

                if (Current().type == TokenType.Numeric)
                    Number();

                if (isArithmeticSymbol(Current()))
                {
                    if (isArithmeticSymbol(Lookahead(-1)))
                    {
                        string invalidArithmeticSymbol = """
                        Invalid token after an arithmetic operator, expected a numeric literal or identifier instead of another arithmetic operator
                        """;

                        ErrorHandler.Parser.Report(fileName, Current(), "general", invalidArithmeticSymbol);
                        ErrorHandler.Parser.PrettyError(fileName, Current());
                    }

                    if (Lookahead(1).type != TokenType.Numeric && Lookahead(1).type != TokenType.Identifier)
                    {
                        string invalidArithmeticSymbol = """
                        Invalid arithmetic expression, expected a numeric literal or identifier after this operator.
                        Arithmetic expressions cannot end with an operator
                        """;

                        ErrorHandler.Parser.Report(fileName, Current(), "general", invalidArithmeticSymbol);
                        ErrorHandler.Parser.PrettyError(fileName, Current());
                    }

                    Symbol();
                }

                if (CurrentEquals("."))
                    return;

                if (Current().type == TokenType.Symbol && !isArithmeticSymbol(Current()))
                {
                    string invalidArithmeticSymbol = """
                    Invalid symbol in this arithmetic expression. Valid operators are: +, -, *, /, **, ( and )
                    """;

                    ErrorHandler.Parser.Report(fileName, Current(), "general", invalidArithmeticSymbol);
                    ErrorHandler.Parser.PrettyError(fileName, Current());
                }
            }
        }

        void Condition(string delimiter)
        {
            Token current = Current();
            List<Token> expression = new();
            while (Current().context != TokenContext.IsStatement && !CurrentEquals(delimiter))
            {
                if (CurrentEquals("NOT") && (LookaheadEquals(1, ">") || LookaheadEquals(1, "<")))
                {
                    Token combined = new($"{Current().value} {Lookahead(1).value}", TokenType.Symbol, Current().line, Current().column);
                    expression.Add(combined);
                    analyzed.Add(combined);
                    Continue();
                    Continue();
                }
                else
                {
                    expression.Add(Current());
                    Expected(Current().value);
                }
            }

            if(!Helpers.IsBalanced(expression))
            {
                string expressionNotBalancedError = """
                This expression is not balanced, one or more parenthesis to not have their matching opening or closing pair, it is an invalid expression
                """;

                ErrorHandler.Parser.Report(fileName, expression[0], "general", expressionNotBalancedError);
                ErrorHandler.Parser.PrettyError(fileName, expression[0]);
            }

            List<Token> ShuntingYard = Helpers.ShuntingYard(expression, Helpers.BooleanPrecedence);
            
            if (!Helpers.EvaluatePostfix(ShuntingYard, Helpers.BooleanPrecedence, out Token error))
            {
                string expressionNotValidError = """
                This expression cannot be correctly evaluated. Please make sure that all operators have their matching operands.
                """;

                ErrorHandler.Parser.Report(fileName, error, "general", expressionNotValidError);
                ErrorHandler.Parser.PrettyError(fileName, error);
            }
        }

        void Identifier()
        {
            Token current = Current();
            if (current.type != TokenType.Identifier)
            {
                ErrorHandler.Parser.Report(fileName, Current(), "expected", "identifier");
                ErrorHandler.Parser.PrettyError(fileName, Current());
                Continue();
                return;
            }
            analyzed.Add(current);
            Continue();
            return;
        }

        void Number(string custom = "expected", int position = 0)
        {
            string errorMessage = "string literal";
            string errorType = "expected";
            Token token = Current();
            if (!custom.Equals("expected"))
            {
                errorMessage = custom;
                errorType = "general";
            }

            if (position != 0)
                token = Lookahead(position);

            Token current = Current();
            if (current.type != TokenType.Numeric)
            {
                ErrorHandler.Parser.Report(fileName, token, errorType, errorMessage);
                ErrorHandler.Parser.PrettyError(fileName, token);
                Continue();
                return;
            }
            analyzed.Add(current);
            Continue();
            return;
        }

        void String(string custom = "expected", int position = 0)
        {
            string errorMessage = "string literal";
            string errorType = "expected";
            Token token = Current();
            if (!custom.Equals("expected"))
            {
                errorMessage = custom;
                errorType = "general";
            }

            if (position != 0)
                token = Lookahead(position);

            Token current = Current();
            if (current.type != TokenType.String)
            {
                ErrorHandler.Parser.Report(fileName, token, errorType, errorMessage);
                ErrorHandler.Parser.PrettyError(fileName, token);
                Continue();
                return;
            }
            analyzed.Add(current);
            Continue();
            return;
        }

        void FigurativeLiteral()
        {
            Token current = Current();
            if (current.type != TokenType.FigurativeLiteral)
            {
                ErrorHandler.Parser.Report(fileName, Current(), "expected", "figurative literal");
                ErrorHandler.Parser.PrettyError(fileName, Current());
                Continue();
                return;
            }
            analyzed.Add(current);
            Continue();
            return;
        }

        void Symbol(string custom = "expected", int position = 0)
        {
            string errorMessage = "string literal";
            string errorType = "expected";
            Token token = Current();
            if (!custom.Equals("expected"))
            {
                errorMessage = custom;
                errorType = "general";
            }

            if (position != 0)
                token = Lookahead(position);

            Token current = Current();
            if (current.type != TokenType.Symbol)
            {
                ErrorHandler.Parser.Report(fileName, token, errorType, errorMessage);
                ErrorHandler.Parser.PrettyError(fileName, token);
                Continue();
                return;
            }
            analyzed.Add(current);
            Continue();
            return;
        }

        bool NotIdentifierOrLiteral()
        {
            return Current().type != TokenType.Identifier
                && Current().type != TokenType.Numeric
                && Current().type != TokenType.String;
        }

    }
}
