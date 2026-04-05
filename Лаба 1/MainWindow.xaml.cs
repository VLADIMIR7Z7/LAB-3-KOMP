using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

namespace TextEditor
{
    public partial class MainWindow : Window
    {
        private string currentFilePath = null;
        private bool isTextChanged = false;
        private FStringScanner scanner;
        private FStringParser parser;

        public MainWindow()
        {
            InitializeComponent();
            scanner = new FStringScanner();
            parser = new FStringParser();
            InitializeNewDocument();
            ResultsGrid.ItemsSource = new List<Token>();
            SyntaxErrorsGrid.ItemsSource = new List<SyntaxErrorDisplay>();
        }

        // =========================
        // МОДЕЛИ
        // =========================

        public class Token
        {
            public int Code { get; set; }
            public string Type { get; set; }
            public string Value { get; set; }
            public int Line { get; set; }
            public int StartPosition { get; set; }
            public int EndPosition { get; set; }
            public bool IsError { get; set; }
            public string ErrorMessage { get; set; }
        }

        public class SyntaxErrorDisplay
        {
            public string InvalidFragment { get; set; }
            public string Location { get; set; }
            public string Description { get; set; }
            public string Expected { get; set; }
            public string Found { get; set; }
            public int Line { get; set; }
            public int Position { get; set; }
        }

        public class ParseResult
        {
            public bool Success { get; set; }
            public string Message { get; set; }
            public List<SyntaxErrorDisplay> Errors { get; set; }
            public int ErrorCount { get; set; }
        }

        public static class TokenCodes
        {
            public const int F = 1;            // ключевое слово f
            public const int E = 2;            // экспонента e
            public const int Identifier = 3;   // идентификатор
            public const int Quote = 4;        // "
            public const int OpenBrace = 5;    // {
            public const int CloseBrace = 6;   // }
            public const int Colon = 7;        // :
            public const int Dot = 8;          // .
            public const int Digit = 9;        // 0..9
            public const int Error = 999;      // недопустимый символ
        }

        // =========================
        // ЛЕКСИЧЕСКИЙ АНАЛИЗАТОР
        // =========================

        public class FStringScanner
        {
            private string _input;
            private int _position;
            private int _line;
            private int _lineStart;
            private char _current;
            private List<Token> _tokens;

            public List<Token> Scan(string input)
            {
                _input = input ?? string.Empty;
                _position = 0;
                _line = 1;
                _lineStart = 0;
                _tokens = new List<Token>();
                _current = _input.Length > 0 ? _input[0] : '\0';

                while (_position < _input.Length)
                {
                    if (char.IsWhiteSpace(_current))
                    {
                        HandleWhitespace();
                        continue;
                    }

                    ProcessCharacter();
                }

                return _tokens;
            }

            private void Advance()
            {
                _position++;
                _current = _position < _input.Length ? _input[_position] : '\0';
            }

            private int Pos() => _position - _lineStart + 1;

            private bool IsLatinLowerLetter(char c) => c >= 'a' && c <= 'z';

            private void HandleWhitespace()
            {
                while (_position < _input.Length && char.IsWhiteSpace(_current))
                {
                    if (_current == '\n')
                    {
                        _line++;
                        _lineStart = _position + 1;
                    }

                    Advance();
                }
            }

            private void ProcessCharacter()
            {
                int pos = Pos();

                if (_current == '"')
                {
                    AddToken(TokenCodes.Quote, "кавычка", "\"", pos, pos);
                    Advance();
                    return;
                }

                if (_current == '{')
                {
                    AddToken(TokenCodes.OpenBrace, "открывающая фигурная скобка", "{", pos, pos);
                    Advance();
                    return;
                }

                if (_current == '}')
                {
                    AddToken(TokenCodes.CloseBrace, "закрывающая фигурная скобка", "}", pos, pos);
                    Advance();
                    return;
                }

                if (_current == ':')
                {
                    AddToken(TokenCodes.Colon, "двоеточие", ":", pos, pos);
                    Advance();
                    return;
                }

                if (_current == '.')
                {
                    AddToken(TokenCodes.Dot, "точка", ".", pos, pos);
                    Advance();
                    return;
                }

                if (char.IsDigit(_current))
                {
                    AddToken(TokenCodes.Digit, "цифра", _current.ToString(), pos, pos);
                    Advance();
                    return;
                }

                if (IsLatinLowerLetter(_current))
                {
                    ParseWord();
                    return;
                }

                AddError($"Недопустимый символ '{_current}'", _line, pos);
                Advance();
            }

            private void ParseWord()
            {
                int start = Pos();
                int startLine = _line;
                string value = "";

                while (_position < _input.Length && char.IsLetter(_current))
                {
                    value += _current;
                    Advance();
                }

                // 1. Ровно один символ f в начале строки -> ключевое слово
                if (_tokens.Count == 0 && value == "f")
                {
                    _tokens.Add(new Token
                    {
                        Code = TokenCodes.F,
                        Type = "ключевое слово f",
                        Value = value,
                        Line = startLine,
                        StartPosition = start,
                        EndPosition = start
                    });
                    return;
                }

                // 2. Ровно один символ e после цифры -> экспонента
                if (value == "e" && _tokens.Count > 0 && _tokens.Last().Code == TokenCodes.Digit)
                {
                    _tokens.Add(new Token
                    {
                        Code = TokenCodes.E,
                        Type = "экспонента e",
                        Value = value,
                        Line = startLine,
                        StartPosition = start,
                        EndPosition = start
                    });
                    return;
                }

                // 3. Всё остальное -> идентификатор
                _tokens.Add(new Token
                {
                    Code = TokenCodes.Identifier,
                    Type = "идентификатор",
                    Value = value,
                    Line = startLine,
                    StartPosition = start,
                    EndPosition = start + value.Length - 1
                });
            }

            private void AddToken(int code, string type, string value, int start, int end)
            {
                _tokens.Add(new Token
                {
                    Code = code,
                    Type = type,
                    Value = value,
                    Line = _line,
                    StartPosition = start,
                    EndPosition = end
                });
            }

            private void AddError(string msg, int line, int col)
            {
                _tokens.Add(new Token
                {
                    Code = TokenCodes.Error,
                    Type = "ОШИБКА",
                    Value = _current.ToString(),
                    Line = line,
                    StartPosition = col,
                    EndPosition = col,
                    IsError = true,
                    ErrorMessage = msg
                });
            }
        }

        // =========================
        // СИНТАКСИЧЕСКИЙ АНАЛИЗАТОР
        // Реализация: граф автоматной грамматики
        // Нейтрализация ошибок: метод Айронса
        // =========================

        public class FStringParser
        {
            private enum ParserState
            {
                ExpectF = 0,             // ожидается f
                ExpectOpeningQuote = 1,  // ожидается "
                ExpectOpenBrace = 2,     // ожидается {
                ExpectIdentifier = 3,    // ожидается идентификатор
                ExpectColon = 4,         // ожидается :
                ExpectDot = 5,           // ожидается .
                ExpectFirstDigit = 6,    // ожидается первая цифра
                ExpectMoreDigitsOrE = 7, // ожидается цифра или e
                ExpectCloseBrace = 8,    // ожидается }
                ExpectClosingQuote = 9,  // ожидается "
                Accept = 10              // конец строки
            }

            private List<Token> _tokens;
            private int _position;
            private Token _current;
            private readonly List<SyntaxErrorDisplay> _errors = new List<SyntaxErrorDisplay>();

            public ParseResult Parse(List<Token> tokens)
            {
                _tokens = tokens ?? new List<Token>();
                _position = 0;
                _errors.Clear();
                GetNextToken();

                if (_tokens.Count == 0)
                {
                    AddError("", 1, 1, "ключевое слово 'f'", "конец строки", "Пустая строка");
                    return GetResult();
                }

                ParserState state = ParserState.ExpectF;
                int safetyCounter = 0;

                while (state != ParserState.Accept && safetyCounter < 10000)
                {
                    safetyCounter++;

                    ConsumeLexicalErrors();

                    if (MatchesState(state, _current))
                    {
                        state = ConsumeExpectedToken(state);
                        continue;
                    }

                    AddStateError(state, _current);

                    if (_current == null)
                        break;

                    Recover(ref state);

                    if (_current == null)
                        break;
                }

                ConsumeLexicalErrors();

                if (_current != null)
                {
                    AddError(
                        _current.Value,
                        _current.Line,
                        _current.StartPosition,
                        "конец строки",
                        _current.Value,
                        "Лишние символы после закрывающей кавычки"
                    );
                }

                return GetResult();
            }

            private ParseResult GetResult()
            {
                return new ParseResult
                {
                    Success = _errors.Count == 0,
                    Message = _errors.Count == 0
                        ? "Синтаксических ошибок не найдено"
                        : $"Найдено ошибок: {_errors.Count}",
                    Errors = _errors,
                    ErrorCount = _errors.Count
                };
            }

            private void GetNextToken()
            {
                _current = _position < _tokens.Count ? _tokens[_position++] : null;
            }

            private void ConsumeLexicalErrors()
            {
                while (_current != null && _current.IsError)
                {
                    AddError(
                        _current.Value,
                        _current.Line,
                        _current.StartPosition,
                        "допустимый символ",
                        _current.Value,
                        _current.ErrorMessage
                    );

                    GetNextToken();
                }
            }

            private bool MatchesState(ParserState state, Token token)
            {
                switch (state)
                {
                    case ParserState.ExpectF:
                        return token != null && token.Code == TokenCodes.F;

                    case ParserState.ExpectOpeningQuote:
                        return token != null && token.Code == TokenCodes.Quote;

                    case ParserState.ExpectOpenBrace:
                        return token != null && token.Code == TokenCodes.OpenBrace;

                    case ParserState.ExpectIdentifier:
                        return token != null && token.Code == TokenCodes.Identifier;

                    case ParserState.ExpectColon:
                        return token != null && token.Code == TokenCodes.Colon;

                    case ParserState.ExpectDot:
                        return token != null && token.Code == TokenCodes.Dot;

                    case ParserState.ExpectFirstDigit:
                        return token != null && token.Code == TokenCodes.Digit;

                    case ParserState.ExpectMoreDigitsOrE:
                        return token != null && (token.Code == TokenCodes.Digit || token.Code == TokenCodes.E);

                    case ParserState.ExpectCloseBrace:
                        return token != null && token.Code == TokenCodes.CloseBrace;

                    case ParserState.ExpectClosingQuote:
                        return token != null && token.Code == TokenCodes.Quote;

                    case ParserState.Accept:
                        return token == null;

                    default:
                        return false;
                }
            }

            private ParserState ConsumeExpectedToken(ParserState state)
            {
                switch (state)
                {
                    case ParserState.ExpectF:
                        GetNextToken();
                        return ParserState.ExpectOpeningQuote;

                    case ParserState.ExpectOpeningQuote:
                        GetNextToken();
                        return ParserState.ExpectOpenBrace;

                    case ParserState.ExpectOpenBrace:
                        GetNextToken();
                        return ParserState.ExpectIdentifier;

                    case ParserState.ExpectIdentifier:
                        GetNextToken();
                        return ParserState.ExpectColon;

                    case ParserState.ExpectColon:
                        GetNextToken();
                        return ParserState.ExpectDot;

                    case ParserState.ExpectDot:
                        GetNextToken();
                        return ParserState.ExpectFirstDigit;

                    case ParserState.ExpectFirstDigit:
                        GetNextToken();
                        return ParserState.ExpectMoreDigitsOrE;

                    case ParserState.ExpectMoreDigitsOrE:
                        if (_current != null && _current.Code == TokenCodes.Digit)
                        {
                            GetNextToken();
                            return ParserState.ExpectMoreDigitsOrE;
                        }

                        if (_current != null && _current.Code == TokenCodes.E)
                        {
                            GetNextToken();
                            return ParserState.ExpectCloseBrace;
                        }

                        return ParserState.ExpectMoreDigitsOrE;

                    case ParserState.ExpectCloseBrace:
                        GetNextToken();
                        return ParserState.ExpectClosingQuote;

                    case ParserState.ExpectClosingQuote:
                        GetNextToken();
                        return ParserState.Accept;

                    default:
                        return ParserState.Accept;
                }
            }

            private void Recover(ref ParserState state)
            {
                switch (state)
                {
                    case ParserState.ExpectF:
                        // Ищем либо f, либо ближайшую открывающую кавычку,
                        // чтобы продолжить разбор конструкции
                        SkipUntil(TokenCodes.F, TokenCodes.Quote);

                        if (_current != null && _current.Code == TokenCodes.F)
                            state = ParserState.ExpectF;
                        else if (_current != null && _current.Code == TokenCodes.Quote)
                            state = ParserState.ExpectOpeningQuote;
                        break;

                    case ParserState.ExpectOpeningQuote:
                        SkipUntil(TokenCodes.Quote, TokenCodes.OpenBrace);

                        if (_current != null && _current.Code == TokenCodes.Quote)
                            state = ParserState.ExpectOpeningQuote;
                        else if (_current != null && _current.Code == TokenCodes.OpenBrace)
                            state = ParserState.ExpectOpenBrace;
                        break;

                    case ParserState.ExpectOpenBrace:
                        SkipUntil(TokenCodes.OpenBrace, TokenCodes.Identifier, TokenCodes.Colon);

                        if (_current != null && _current.Code == TokenCodes.OpenBrace)
                            state = ParserState.ExpectOpenBrace;
                        else if (_current != null && _current.Code == TokenCodes.Identifier)
                            state = ParserState.ExpectIdentifier;
                        else if (_current != null && _current.Code == TokenCodes.Colon)
                            state = ParserState.ExpectColon;
                        break;

                    case ParserState.ExpectIdentifier:
                        SkipUntil(TokenCodes.Identifier, TokenCodes.Colon);

                        if (_current != null && _current.Code == TokenCodes.Identifier)
                            state = ParserState.ExpectIdentifier;
                        else if (_current != null && _current.Code == TokenCodes.Colon)
                            state = ParserState.ExpectColon;
                        break;

                    case ParserState.ExpectColon:
                        SkipUntil(TokenCodes.Colon, TokenCodes.Dot);

                        if (_current != null && _current.Code == TokenCodes.Colon)
                            state = ParserState.ExpectColon;
                        else if (_current != null && _current.Code == TokenCodes.Dot)
                            state = ParserState.ExpectDot;
                        break;

                    case ParserState.ExpectDot:
                        SkipUntil(TokenCodes.Dot, TokenCodes.Digit, TokenCodes.E, TokenCodes.CloseBrace);

                        if (_current != null && _current.Code == TokenCodes.Dot)
                            state = ParserState.ExpectDot;
                        else if (_current != null && _current.Code == TokenCodes.Digit)
                            state = ParserState.ExpectFirstDigit;
                        else if (_current != null && _current.Code == TokenCodes.E)
                            state = ParserState.ExpectMoreDigitsOrE;
                        else if (_current != null && _current.Code == TokenCodes.CloseBrace)
                            state = ParserState.ExpectCloseBrace;
                        break;

                    case ParserState.ExpectFirstDigit:
                        SkipUntil(TokenCodes.Digit, TokenCodes.E, TokenCodes.CloseBrace);

                        if (_current != null && _current.Code == TokenCodes.Digit)
                            state = ParserState.ExpectFirstDigit;
                        else if (_current != null && _current.Code == TokenCodes.E)
                            state = ParserState.ExpectMoreDigitsOrE;
                        else if (_current != null && _current.Code == TokenCodes.CloseBrace)
                            state = ParserState.ExpectCloseBrace;
                        break;

                    case ParserState.ExpectMoreDigitsOrE:
                        SkipUntil(TokenCodes.Digit, TokenCodes.E, TokenCodes.CloseBrace);

                        if (_current != null && _current.Code == TokenCodes.Digit)
                            state = ParserState.ExpectMoreDigitsOrE;
                        else if (_current != null && _current.Code == TokenCodes.E)
                            state = ParserState.ExpectMoreDigitsOrE;
                        else if (_current != null && _current.Code == TokenCodes.CloseBrace)
                            state = ParserState.ExpectCloseBrace;
                        break;

                    case ParserState.ExpectCloseBrace:
                        SkipUntil(TokenCodes.CloseBrace, TokenCodes.Quote);

                        if (_current != null && _current.Code == TokenCodes.CloseBrace)
                            state = ParserState.ExpectCloseBrace;
                        else if (_current != null && _current.Code == TokenCodes.Quote)
                            state = ParserState.ExpectClosingQuote;
                        break;

                    case ParserState.ExpectClosingQuote:
                        SkipUntil(TokenCodes.Quote);

                        if (_current != null && _current.Code == TokenCodes.Quote)
                            state = ParserState.ExpectClosingQuote;
                        break;
                }
            }

            private void SkipUntil(params int[] tokenCodes)
            {
                while (_current != null)
                {
                    if (_current.IsError)
                    {
                        AddError(
                            _current.Value,
                            _current.Line,
                            _current.StartPosition,
                            "допустимый символ",
                            _current.Value,
                            _current.ErrorMessage
                        );
                        GetNextToken();
                        continue;
                    }

                    if (tokenCodes.Contains(_current.Code))
                        return;

                    GetNextToken();
                }
            }

            private void AddStateError(ParserState state, Token token)
            {
                string found = token?.Value ?? "конец строки";
                int line = token?.Line ?? 1;
                int pos = token?.StartPosition ?? 1;

                switch (state)
                {
                    case ParserState.ExpectF:
                        AddError(found, line, pos, "ключевое слово 'f'", found, "Строка должна начинаться с 'f'");
                        break;

                    case ParserState.ExpectOpeningQuote:
                        AddError(found, line, pos, "открывающая кавычка '\"'", found, "Ожидается '\"' после 'f'");
                        break;

                    case ParserState.ExpectOpenBrace:
                        AddError(found, line, pos, "открывающая фигурная скобка '{'", found, "Ожидается '{' после '\"'");
                        break;

                    case ParserState.ExpectIdentifier:
                        AddError(found, line, pos, "идентификатор", found, "Ожидается идентификатор после '{'");
                        break;

                    case ParserState.ExpectColon:
                        AddError(found, line, pos, "двоеточие ':'", found, "Ожидается ':' после идентификатора");
                        break;

                    case ParserState.ExpectDot:
                        AddError(found, line, pos, "точка '.'", found, "Ожидается '.' после ':'");
                        break;

                    case ParserState.ExpectFirstDigit:
                        AddError(found, line, pos, "цифра", found, "После точки должна быть хотя бы одна цифра");
                        break;

                    case ParserState.ExpectMoreDigitsOrE:
                        AddError(found, line, pos, "цифра или символ 'e'", found, "Ожидается цифра или 'e' после цифр");
                        break;

                    case ParserState.ExpectCloseBrace:
                        AddError(found, line, pos, "закрывающая фигурная скобка '}'", found, "Ожидается '}' после 'e'");
                        break;

                    case ParserState.ExpectClosingQuote:
                        AddError(found, line, pos, "закрывающая кавычка '\"'", found, "Ожидается '\"' после '}'");
                        break;
                }
            }

            private void AddError(string invalidFragment, int line, int position, string expected, string found, string description)
            {
                if (_errors.Any(e => e.Line == line && e.Position == position && e.Description == description))
                    return;

                _errors.Add(new SyntaxErrorDisplay
                {
                    InvalidFragment = string.IsNullOrEmpty(invalidFragment) ? found : invalidFragment,
                    Location = $"строка {line}, позиция {position}",
                    Description = description,
                    Expected = expected,
                    Found = found,
                    Line = line,
                    Position = position
                });
            }
        }

        // =========================
        // ОСНОВНАЯ ЛОГИКА ОКНА
        // =========================

        private void InitializeNewDocument()
        {
            EditorBox.Document = new FlowDocument();
            EditorBox.Focus();
            UpdateStatusBar();
        }

        private void CreateFile_Click(object sender, RoutedEventArgs e)
        {
            if (PromptSaveChanges())
            {
                EditorBox.Document = new FlowDocument();
                currentFilePath = null;
                isTextChanged = false;
                UpdateStatusBar();
                StatusText.Text = "Создан новый документ";
                ClearResults();
            }
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            if (!PromptSaveChanges()) return;

            OpenFileDialog openDialog = new OpenFileDialog
            {
                Filter = "Текстовые файлы (*.txt)|*.txt|Все файлы (*.*)|*.*",
                Title = "Открыть файл"
            };

            if (openDialog.ShowDialog() == true)
            {
                try
                {
                    string content = File.ReadAllText(openDialog.FileName);
                    EditorBox.Document = new FlowDocument();
                    EditorBox.AppendText(content);
                    currentFilePath = openDialog.FileName;
                    isTextChanged = false;
                    FileInfoText.Text = Path.GetFileName(currentFilePath);
                    StatusText.Text = $"Файл загружен: {Path.GetFileName(currentFilePath)}";
                    ClearResults();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при открытии файла: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void SaveFile_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(currentFilePath))
                SaveAsFile_Click(sender, e);
            else
                SaveFile(currentFilePath);
        }

        private void SaveAsFile_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveDialog = new SaveFileDialog
            {
                Filter = "Текстовые файлы (*.txt)|*.txt|Все файлы (*.*)|*.*",
                Title = "Сохранить файл как"
            };

            if (saveDialog.ShowDialog() == true)
                SaveFile(saveDialog.FileName);
        }

        private void SaveFile(string filePath)
        {
            try
            {
                TextRange range = new TextRange(EditorBox.Document.ContentStart, EditorBox.Document.ContentEnd);
                File.WriteAllText(filePath, range.Text);
                currentFilePath = filePath;
                isTextChanged = false;
                FileInfoText.Text = Path.GetFileName(currentFilePath);
                StatusText.Text = "Файл сохранен";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при сохранении файла: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            if (PromptSaveChanges())
                Application.Current.Shutdown();
        }

        private bool PromptSaveChanges()
        {
            if (!isTextChanged) return true;

            var result = MessageBox.Show("Сохранить изменения в файле?", "Сохранение", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                SaveFile_Click(null, null);
                return true;
            }

            return result != MessageBoxResult.Cancel;
        }

        private void Undo_Click(object sender, RoutedEventArgs e) => EditorBox.Undo();
        private void Redo_Click(object sender, RoutedEventArgs e) => EditorBox.Redo();
        private void Cut_Click(object sender, RoutedEventArgs e) => EditorBox.Cut();
        private void Copy_Click(object sender, RoutedEventArgs e) => EditorBox.Copy();
        private void Paste_Click(object sender, RoutedEventArgs e) => EditorBox.Paste();
        private void Delete_Click(object sender, RoutedEventArgs e) => EditorBox.Selection.Text = string.Empty;
        private void SelectAll_Click(object sender, RoutedEventArgs e) => EditorBox.SelectAll();

        private void TaskDescription_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow(
                "Постановка задачи",
                "ЛАБОРАТОРНАЯ РАБОТА №3\n" +
                "Разработка синтаксического анализатора (парсера)\n" +
                "Вариант: f\"{id:.Ne}\"\n\n" +
                "Парсер реализован на основе графа автоматной грамматики.\n" +
                "Нейтрализация синтаксических ошибок выполняется методом Айронса."
            );
        }

        private void Grammar_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow(
                "Грамматика",
                "G[Start] для строк вида f\"{id:.Ne}\"\n\n" +
                "Start      → f FPrefix\n" +
                "FPrefix    → \" OpenQuote\n" +
                "OpenQuote  → { OpenBrace\n" +
                "OpenBrace  → Identifier\n" +
                "Identifier → Letter Identifier | : AfterColon\n" +
                "AfterColon → . AfterDot\n" +
                "AfterDot   → Digit Digits\n" +
                "Digits     → Digit Digits | e Exponent\n" +
                "Exponent   → } CloseBrace\n" +
                "CloseBrace → \" CloseQuote\n" +
                "CloseQuote → End\n" +
                "End        → ε"
            );
        }

        private void GrammarClassification_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow("Классификация", "Тип 3 по Хомскому. Регулярная (автоматная, праволинейная) грамматика.");
        }

        private void AnalysisMethod_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow(
                "Метод анализа",
                "Метод анализа: граф автоматной грамматики.\n\n" +
                "Программа выполняет обход по состояниям автомата:\n" +
                "f → \" → { → id → : → . → цифры → e → } → \".\n\n" +
                "При ошибке используется восстановление методом Айронса:\n" +
                "анализатор фиксирует ошибку и продолжает разбор с ближайшего допустимого состояния."
            );
        }

        private void TestExample_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow(
                "Тестовые примеры",
                "Корректные:\n" +
                "f\"{m:.2e}\"\n" +
                "f\"{hello:.123e}\"\n" +
                "f\"{e:.1e}\"\n\n" +
                "Некорректные:\n" +
                "(пустая строка)\n" +
                "\"{name:.5e}\"\n" +
                "f\"{abc:123e}\"\n" +
                "f\"{:.123e}\"\n" +
                "f\"{abc:.e}\"\n" +
                "f\"{abc:.e+\""
            );
        }

        private void References_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow(
                "Литература",
                "1. Ахо А., Лам М., Сети Р., Ульман Д. Компиляторы.\n" +
                "2. Вирт Н. Построение компиляторов.\n" +
                "3. Методические указания по лабораторной работе №3."
            );
        }

        private void SourceCode_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow(
                "Исходный код",
                "MainWindow.xaml — интерфейс приложения\n" +
                "MainWindow.xaml.cs — логика редактора, лексический и синтаксический анализ"
            );
        }

        private void ShowInfoWindow(string title, string content)
        {
            Window infoWindow = new Window
            {
                Title = title,
                Content = new TextBox
                {
                    Text = content,
                    IsReadOnly = true,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(10),
                    FontFamily = new FontFamily("Consolas"),
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto
                },
                Width = 650,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this
            };

            infoWindow.ShowDialog();
        }

        private void ClearResults()
        {
            ResultsGrid.ItemsSource = null;
            ResultsGrid.ItemsSource = new List<Token>();

            SyntaxErrorsGrid.ItemsSource = null;
            SyntaxErrorsGrid.ItemsSource = new List<SyntaxErrorDisplay>();

            ErrorCountText.Text = "Общее количество ошибок: 0";

            TextRange range = new TextRange(EditorBox.Document.ContentStart, EditorBox.Document.ContentEnd);
            range.ApplyPropertyValue(TextElement.BackgroundProperty, null);
        }

        private void StartAnalysis_Click(object sender, RoutedEventArgs e)
        {
            ClearResults();
            StatusText.Text = "Анализ...";

            TextRange range = new TextRange(EditorBox.Document.ContentStart, EditorBox.Document.ContentEnd);
            string text = range.Text;

            try
            {
                // 1. Лексический анализ
                var tokens = scanner.Scan(text);
                ResultsGrid.ItemsSource = tokens;

                // 2. Синтаксический анализ
                var parseResult = parser.Parse(tokens);
                SyntaxErrorsGrid.ItemsSource = parseResult.Errors;

                if (parseResult.Success)
                {
                    ErrorCountText.Text = "Синтаксических ошибок не найдено";
                    StatusText.Text = "✓ Успешно! Синтаксических ошибок не найдено";
                }
                else
                {
                    ErrorCountText.Text = $"Общее количество ошибок: {parseResult.ErrorCount}";
                    StatusText.Text = $"✗ Ошибок: {parseResult.ErrorCount}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка анализа: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Ошибка анализа";
            }
        }

        private void NavigateToPosition(int line, int position)
        {
            TextPointer pointer = EditorBox.Document.ContentStart;

            for (int i = 1; i < line; i++)
            {
                TextPointer nextLine = pointer.GetLineStartPosition(1);
                if (nextLine == null)
                    break;

                pointer = nextLine;
            }

            for (int i = 1; i < position; i++)
            {
                TextPointer next = pointer.GetNextInsertionPosition(LogicalDirection.Forward);
                if (next == null)
                    break;

                pointer = next;
            }

            EditorBox.CaretPosition = pointer;
            EditorBox.Focus();
        }

        private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ResultsGrid.SelectedItem is Token token)
            {
                NavigateToPosition(token.Line, token.StartPosition);

                if (token.IsError)
                    StatusText.Text = $"Переход к лексической ошибке: строка {token.Line}, позиция {token.StartPosition}";
                else
                    StatusText.Text = $"Переход к лексеме: строка {token.Line}, позиция {token.StartPosition}";
            }
        }

        private void SyntaxErrorsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (SyntaxErrorsGrid.SelectedItem is SyntaxErrorDisplay error)
            {
                NavigateToPosition(error.Line, error.Position);
                StatusText.Text = $"Переход к ошибке: {error.Location}";
            }
        }

        // Эти два обработчика нужны, если замените MouseDoubleClick на SelectionChanged в XAML.
        private void ResultsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ResultsGrid.SelectedItem is Token token)
            {
                NavigateToPosition(token.Line, token.StartPosition);

                if (token.IsError)
                    StatusText.Text = $"Переход к лексической ошибке: строка {token.Line}, позиция {token.StartPosition}";
                else
                    StatusText.Text = $"Переход к лексеме: строка {token.Line}, позиция {token.StartPosition}";
            }
        }

        private void SyntaxErrorsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SyntaxErrorsGrid.SelectedItem is SyntaxErrorDisplay error)
            {
                NavigateToPosition(error.Line, error.Position);
                StatusText.Text = $"Переход к ошибке: {error.Location}";
            }
        }

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow(
                "Справка",
                "F5 — запуск анализа\n" +
                "Ctrl+N — новый файл\n" +
                "Ctrl+O — открыть\n" +
                "Ctrl+S — сохранить\n\n" +
                "Допустимый формат:\n" +
                "f\"{id:.Ne}\""
            );
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow(
                "О программе",
                "ЛАБОРАТОРНАЯ РАБОТА №3\n" +
                "Синтаксический анализатор\n" +
                "Вариант: f\"{id:.Ne}\"\n" +
                "Метод анализа: граф автоматной грамматики\n" +
                "Нейтрализация ошибок: метод Айронса"
            );
        }

        private void EditorBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            isTextChanged = true;
            UpdateStatusBar();
            ClearResults();
        }

        private void EditorBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            UpdateStatusBar();
        }

        private void UpdateStatusBar()
        {
            try
            {
                TextPointer caret = EditorBox.CaretPosition;
                TextPointer lineStart = caret.GetLineStartPosition(0);

                int line = 1;
                TextPointer walker = lineStart;

                while (walker != null)
                {
                    TextPointer prev = walker.GetLineStartPosition(-1);
                    if (prev == null)
                        break;

                    walker = prev;
                    line++;
                }

                int col = 1;
                TextPointer temp = lineStart;

                while (temp != null && temp.CompareTo(caret) < 0)
                {
                    TextPointer next = temp.GetNextInsertionPosition(LogicalDirection.Forward);
                    if (next == null)
                        break;

                    temp = next;
                    col++;
                }

                CursorPositionText.Text = $"Стр: {line}, Стб: {col}";
            }
            catch
            {
                CursorPositionText.Text = "Стр: 1, Стб: 1";
            }

            FileInfoText.Text = string.IsNullOrEmpty(currentFilePath)
                ? "Новый документ"
                : Path.GetFileName(currentFilePath);

            if (isTextChanged && !FileInfoText.Text.EndsWith("*"))
                FileInfoText.Text += "*";
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!PromptSaveChanges())
                e.Cancel = true;

            base.OnClosing(e);
        }
    }
}