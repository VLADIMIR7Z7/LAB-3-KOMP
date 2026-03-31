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

        // Лексический анализатор
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
                _input = input ?? "";
                _position = 0;
                _line = 1;
                _lineStart = 0;
                _tokens = new List<Token>();

                if (_input.Length > 0) _current = _input[0];

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
                if (_position < _input.Length) _current = _input[_position];
                else _current = '\0';
            }

            private int Pos() => _position - _lineStart + 1;

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
                if (_current == '"')
                {
                    _tokens.Add(new Token { Code = 6, Type = "кавычка", Value = _current.ToString(), Line = _line, StartPosition = Pos(), EndPosition = Pos() });
                    Advance();
                    return;
                }
                if (_current == '{')
                {
                    _tokens.Add(new Token { Code = 4, Type = "открывающая скобка", Value = _current.ToString(), Line = _line, StartPosition = Pos(), EndPosition = Pos() });
                    Advance();
                    return;
                }
                if (_current == '}')
                {
                    _tokens.Add(new Token { Code = 5, Type = "закрывающая скобка", Value = _current.ToString(), Line = _line, StartPosition = Pos(), EndPosition = Pos() });
                    Advance();
                    return;
                }
                if (_current == ':')
                {
                    _tokens.Add(new Token { Code = 8, Type = "разделитель", Value = _current.ToString(), Line = _line, StartPosition = Pos(), EndPosition = Pos() });
                    Advance();
                    return;
                }
                if (_current == '.')
                {
                    _tokens.Add(new Token { Code = 8, Type = "точка", Value = _current.ToString(), Line = _line, StartPosition = Pos(), EndPosition = Pos() });
                    Advance();
                    return;
                }
                if (char.IsDigit(_current))
                {
                    _tokens.Add(new Token { Code = 8, Type = "цифра", Value = _current.ToString(), Line = _line, StartPosition = Pos(), EndPosition = Pos() });
                    Advance();
                    return;
                }
                if (char.IsLetter(_current))
                {
                    ParseLetterSequence();
                    return;
                }
                // Недопустимый символ - создаем токен ошибки
                AddError($"Недопустимый символ '{_current}'", _line, Pos());
                Advance();
            }

            private void ParseLetterSequence()
            {
                int start = Pos();
                int startLine = _line;
                string sequence = "";

                while (_position < _input.Length && char.IsLetter(_current))
                {
                    sequence += _current;
                    Advance();
                }

                if (sequence.Length == 1)
                {
                    char letter = char.ToLower(sequence[0]);
                    if (letter == 'f')
                    {
                        _tokens.Add(new Token { Code = 1, Type = "ключевое слово f", Value = sequence, Line = startLine, StartPosition = start, EndPosition = start + sequence.Length - 1 });
                    }
                    else if (letter == 'e')
                    {
                        _tokens.Add(new Token { Code = 2, Type = "экспонента e", Value = sequence, Line = startLine, StartPosition = start, EndPosition = start + sequence.Length - 1 });
                    }
                    else
                    {
                        _tokens.Add(new Token { Code = 3, Type = "идентификатор", Value = sequence, Line = startLine, StartPosition = start, EndPosition = start + sequence.Length - 1 });
                    }
                }
                else
                {
                    _tokens.Add(new Token { Code = 3, Type = "идентификатор", Value = sequence, Line = startLine, StartPosition = start, EndPosition = start + sequence.Length - 1 });
                }
            }

            private void AddError(string msg, int line, int col)
            {
                _tokens.Add(new Token { Code = 999, Type = "ОШИБКА", Value = _current.ToString(), Line = line, StartPosition = col, EndPosition = col, IsError = true, ErrorMessage = msg });
            }
        }

        // Синтаксический анализатор 

        // Синтаксический анализатор (парсер) - строго по грамматике с методом Айронса
        public class FStringParser
        {
            private List<Token> _tokens;
            private int _position;
            private Token _current;
            private List<SyntaxErrorDisplay> _errors;
            private int _currentLine;
            private int _currentPosition;

            // Множества синхронизации для метода Айронса
            private readonly HashSet<int> _syncSetStart = new HashSet<int> { 1 };      // f
            private readonly HashSet<int> _syncSetFPrefix = new HashSet<int> { 6 };    // "
            private readonly HashSet<int> _syncSetOpenQuote = new HashSet<int> { 4 };  // {
            private readonly HashSet<int> _syncSetOpenBrace = new HashSet<int> { 3, 8 }; // identifier или :
            private readonly HashSet<int> _syncSetAfterColon = new HashSet<int> { 8 };  // .
            private readonly HashSet<int> _syncSetAfterDot = new HashSet<int> { 8 };    // digit
            private readonly HashSet<int> _syncSetDigits = new HashSet<int> { 2 };      // e
            private readonly HashSet<int> _syncSetExponent = new HashSet<int> { 5 };    // }
            private readonly HashSet<int> _syncSetCloseBrace = new HashSet<int> { 6 };  // "
            private readonly HashSet<int> _syncSetAll = new HashSet<int> { 1, 6, 4, 5, 3, 8, 2 };

            public ParseResult Parse(List<Token> tokens)
            {
                _tokens = tokens ?? new List<Token>();
                _position = 0;
                _errors = new List<SyntaxErrorDisplay>();

                if (_tokens.Count == 0)
                {
                    AddError("", 1, 1, "ключевое слово 'f'", "конец строки", "Пустая строка");
                    return GetResult();
                }

                GetNextToken();
                Start();

                return GetResult();
            }

            private ParseResult GetResult() => new ParseResult
            {
                Success = _errors.Count == 0,
                Message = _errors.Count == 0 ? "Синтаксических ошибок не найдено" : $"Найдено ошибок: {_errors.Count}",
                Errors = _errors,
                ErrorCount = _errors.Count
            };

            private void AddError(string invalidFragment, int line, int position, string expected, string found, string description)
            {
                // Проверка на дубликаты ошибок в одной позиции
                if (_errors.Any(e => e.Line == line && e.Position == position))
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

            private void GetNextToken()
            {
                if (_position < _tokens.Count)
                {
                    _current = _tokens[_position];
                    _currentLine = _current.Line;
                    _currentPosition = _current.StartPosition;
                    _position++;
                }
                else
                {
                    _current = null;
                }
            }

            // Пропуск токенов до синхронизирующего множества (метод Айронса)
            private void SkipToSyncSet(HashSet<int> syncSet)
            {
                while (_current != null && !syncSet.Contains(_current.Code) && !_current.IsError)
                {
                    GetNextToken();
                }
            }

            // Проверка на конец строки
            private bool IsEndOfInput() => _current == null;

            // === Грамматические правила (строго по грамматике) ===

            // Start → f FPrefix
            private void Start()
            {
                // Обработка пустой строки уже сделана в Parse

                // Если нет токена f
                if (_current == null || _current.Code != 1)
                {
                    string found = _current?.Value ?? "конец строки";
                    AddError(found, _current?.Line ?? 1, _current?.StartPosition ?? 1,
                             "ключевое слово 'f'", found, "Строка должна начинаться с 'f'");

                    // Метод Айронса: пропускаем до f
                    SkipToSyncSet(_syncSetStart);

                    if (_current != null && _current.Code == 1)
                        GetNextToken();
                    else
                        return;
                }
                else
                {
                    GetNextToken(); // пропускаем f
                }

                FPrefix();
            }

            // FPrefix → " OpenQuote
            private void FPrefix()
            {
                if (_current == null || _current.Code != 6)
                {
                    string found = _current?.Value ?? "конец строки";
                    AddError(found, _current?.Line ?? 1, _current?.StartPosition ?? 1,
                             "открывающая кавычка '\"'", found, "Ожидается '\"' после 'f'");

                    SkipToSyncSet(_syncSetFPrefix);

                    if (_current != null && _current.Code == 6)
                        GetNextToken();
                    else
                        return;
                }
                else
                {
                    GetNextToken(); // пропускаем "
                }

                OpenQuote();
            }

            // OpenQuote → { OpenBrace
            private void OpenQuote()
            {
                if (_current == null || _current.Code != 4)
                {
                    string found = _current?.Value ?? "конец строки";
                    AddError(found, _current?.Line ?? 1, _current?.StartPosition ?? 1,
                             "открывающая скобка '{'", found, "Ожидается '{' после '\"'");

                    SkipToSyncSet(_syncSetOpenQuote);

                    if (_current != null && _current.Code == 4)
                        GetNextToken();
                    else
                        return;
                }
                else
                {
                    GetNextToken(); // пропускаем {
                }

                OpenBrace();
            }

            // OpenBrace → Letter Identifier
            private void OpenBrace()
            {
                // Должна быть буква (идентификатор)
                if (_current == null || _current.Code != 3)
                {
                    string found = _current?.Value ?? "конец строки";
                    AddError(found, _current?.Line ?? 1, _current?.StartPosition ?? 1,
                             "идентификатор (буква)", found, "Ожидается идентификатор после '{'");

                    SkipToSyncSet(_syncSetOpenBrace);

                    // Если нашли идентификатор - обрабатываем
                    if (_current != null && _current.Code == 3)
                    {
                        // продолжаем
                    }
                    else
                    {
                        // Если нет идентификатора, пытаемся найти ':'
                        if (_current != null && _current.Code == 8 && _current.Value == ":")
                        {
                            // пустой идентификатор - ошибка уже записана, продолжаем
                        }
                        else
                        {
                            return;
                        }
                    }
                }

                Identifier();
            }

            // Identifier → Letter Identifier | : AfterColon
            private void Identifier()
            {
                // Собираем все буквы (одну или более)
                bool hasLetter = false;
                while (_current != null && _current.Code == 3)
                {
                    hasLetter = true;
                    GetNextToken();
                }

                // Проверяем, что был хотя бы один идентификатор
                if (!hasLetter)
                {
                    string found = _current?.Value ?? "конец строки";
                    AddError(found, _current?.Line ?? 1, _current?.StartPosition ?? 1,
                             "идентификатор", found, "Ожидается хотя бы одна буква");
                }

                // Теперь должен быть :
                if (_current == null || _current.Code != 8 || _current.Value != ":")
                {
                    string found = _current?.Value ?? "конец строки";
                    AddError(found, _current?.Line ?? 1, _current?.StartPosition ?? 1,
                             "разделитель ':'", found, "Ожидается ':' после идентификатора");

                    SkipToSyncSet(_syncSetAll);
                    return;
                }
                else
                {
                    GetNextToken(); // пропускаем :
                }

                AfterColon();
            }

            // AfterColon → . AfterDot
            private void AfterColon()
            {
                if (_current == null || _current.Code != 8 || _current.Value != ".")
                {
                    string found = _current?.Value ?? "конец строки";
                    AddError(found, _current?.Line ?? 1, _current?.StartPosition ?? 1,
                             "точка '.'", found, "Ожидается '.' после ':'");

                    SkipToSyncSet(_syncSetAfterColon);
                    return;
                }
                else
                {
                    GetNextToken(); // пропускаем .
                }

                AfterDot();
            }

            // AfterDot → FirstDigit Digits
            private void AfterDot()
            {
                // Первая цифра
                if (_current == null || _current.Code != 8 || !char.IsDigit(_current.Value[0]))
                {
                    string found = _current?.Value ?? "конец строки";
                    AddError(found, _current?.Line ?? 1, _current?.StartPosition ?? 1,
                             "цифра", found, "После точки должна быть хотя бы одна цифра");

                    SkipToSyncSet(_syncSetAfterDot);

                    // Если нашли цифру - продолжаем
                    if (_current != null && _current.Code == 8 && char.IsDigit(_current.Value[0]))
                    {
                        GetNextToken();
                    }
                    else
                    {
                        return;
                    }
                }
                else
                {
                    GetNextToken(); // пропускаем первую цифру
                }

                Digits();
            }

            // Digits → 0-9 Digits | e Exponent
            private void Digits()
            {
                // Собираем все последующие цифры
                while (_current != null && _current.Code == 8 && char.IsDigit(_current.Value[0]))
                {
                    GetNextToken();
                }

                // Теперь должна быть e
                if (_current == null || _current.Code != 2)
                {
                    string found = _current?.Value ?? "конец строки";
                    AddError(found, _current?.Line ?? 1, _current?.StartPosition ?? 1,
                             "экспонента 'e'", found, "Ожидается 'e' после цифр");

                    SkipToSyncSet(_syncSetDigits);

                    if (_current != null && _current.Code == 2)
                        GetNextToken();
                    else
                        return;
                }
                else
                {
                    GetNextToken(); // пропускаем e
                }

                Exponent();
            }

            // Exponent → } CloseBrace
            private void Exponent()
            {
                if (_current == null || _current.Code != 5)
                {
                    string found = _current?.Value ?? "конец строки";
                    AddError(found, _current?.Line ?? 1, _current?.StartPosition ?? 1,
                             "закрывающая скобка '}'", found, "Ожидается '}' после 'e'");

                    SkipToSyncSet(_syncSetExponent);

                    if (_current != null && _current.Code == 5)
                        GetNextToken();
                    else
                        return;
                }
                else
                {
                    GetNextToken(); // пропускаем }
                }

                CloseBrace();
            }

            // CloseBrace → " CloseQuote
            private void CloseBrace()
            {
                if (_current == null || _current.Code != 6)
                {
                    string found = _current?.Value ?? "конец строки";
                    AddError(found, _current?.Line ?? 1, _current?.StartPosition ?? 1,
                             "закрывающая кавычка '\"'", found, "Ожидается '\"' после '}'");

                    SkipToSyncSet(_syncSetCloseBrace);

                    if (_current != null && _current.Code == 6)
                        GetNextToken();
                    else
                        return;
                }
                else
                {
                    GetNextToken(); // пропускаем "
                }

                CloseQuote();
            }

            // CloseQuote → End (ε)
            private void CloseQuote()
            {
                // После закрывающей кавычки ничего не должно быть
                if (_current != null && !_current.IsError)
                {
                    AddError(_current.Value, _current.Line, _current.StartPosition,
                             "конец строки", _current.Value, "Лишние символы после закрывающей кавычки");

                    // Пропускаем все оставшиеся токены
                    while (_current != null)
                    {
                        GetNextToken();
                    }
                }
            }
        }

        public MainWindow()
        {
            InitializeComponent();
            scanner = new FStringScanner();
            parser = new FStringParser();
            InitializeNewDocument();
            ResultsGrid.ItemsSource = new List<Token>();
            SyntaxErrorsGrid.ItemsSource = new List<SyntaxErrorDisplay>();
        }

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
            ShowInfoWindow("Постановка задачи", "ЛАБОРАТОРНАЯ РАБОТА №3\nСинтаксический анализатор\nВариант: f\"{id:.Ne}\"");
        }

        private void Grammar_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow("Грамматика", "G[Start]: f\"{id:.Ne}\"\nStart → f FPrefix\nFPrefix → \" OpenQuote\nOpenQuote → { OpenBrace\nOpenBrace → Identifier\nIdentifier → Letter Identifier | : AfterColon\nAfterColon → . AfterDot\nAfterDot → 0-9 Digits\nDigits → 0-9 Digits | e Exponent\nExponent → } CloseBrace\nCloseBrace → \" CloseQuote\nCloseQuote → End → ε");
        }

        private void GrammarClassification_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow("Классификация", "Тип 3 (регулярная грамматика) по Хомскому");
        }

        private void AnalysisMethod_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow("Метод анализа", "Рекурсивный спуск + метод Айронса");
        }

        private void TestExample_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow("Тестовые примеры", "Корректные:\nf\"{m:.2e}\"\nf\"{hello:.123e}\"\n\nНекорректные:\n(пустая строка)\nf\"{m:2e}\"\nf\"{m:.e}\"");
        }

        private void References_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow("Литература", "1. Ахо А. Компиляторы\n2. Вирт Н. Построение компиляторов");
        }

        private void SourceCode_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow("Исходный код", "MainWindow.xaml - интерфейс\nMainWindow.xaml.cs - логика");
        }

        private void ShowInfoWindow(string title, string content)
        {
            Window infoWindow = new Window
            {
                Title = title,
                Content = new TextBox { Text = content, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10), FontFamily = new FontFamily("Consolas"), VerticalScrollBarVisibility = ScrollBarVisibility.Auto },
                Width = 600,
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
                // Лексический анализ
                var tokens = scanner.Scan(text);
                ResultsGrid.ItemsSource = tokens;

                // Синтаксический анализ
                var parseResult = parser.Parse(tokens);
                SyntaxErrorsGrid.ItemsSource = parseResult.Errors;
                ErrorCountText.Text = $"Общее количество ошибок: {parseResult.ErrorCount}";
                StatusText.Text = parseResult.Success ? "✓ Успешно!" : $"✗ Ошибок: {parseResult.ErrorCount}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка: {ex.Message}");
                StatusText.Text = "Ошибка анализа";
            }
        }

        private void NavigateToPosition(int line, int position)
        {
            TextPointer pointer = EditorBox.Document.ContentStart;
            for (int i = 1; i < line; i++)
            {
                pointer = pointer.GetLineStartPosition(1);
                if (pointer == null) break;
            }
            if (pointer != null)
            {
                for (int i = 1; i < position; i++)
                {
                    pointer = pointer.GetNextInsertionPosition(LogicalDirection.Forward);
                    if (pointer == null) break;
                }
                if (pointer != null)
                {
                    EditorBox.CaretPosition = pointer;
                    EditorBox.Focus();
                }
            }
        }

        private void ResultsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (ResultsGrid.SelectedItem is Token token && token.IsError)
            {
                NavigateToPosition(token.Line, token.StartPosition);
                StatusText.Text = $"Переход к ошибке: строка {token.Line}, позиция {token.StartPosition}";
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

        private void Help_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow("Справка", "F5 - запуск анализа\nCtrl+N - новый файл\nCtrl+O - открыть\nCtrl+S - сохранить\nГрамматика: f\"{id:.Ne}\"");
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            ShowInfoWindow("О программе", "ЛАБОРАТОРНАЯ РАБОТА №3\nСинтаксический анализатор\nВариант: f\"{id:.Ne}\"\nАвтор: Петрухно В.К.\nГруппа: АП-327\nДата: 2026");
        }

        private void EditorBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            isTextChanged = true;
            UpdateStatusBar();
            ClearResults();
        }

        private void EditorBox_SelectionChanged(object sender, RoutedEventArgs e) => UpdateStatusBar();

        private void UpdateStatusBar()
        {
            try
            {
                TextPointer caret = EditorBox.CaretPosition;
                if (caret != null)
                {
                    int line = 1;
                    TextPointer ptr = caret.GetLineStartPosition(0);
                    while (ptr != null)
                    {
                        TextPointer prev = ptr.GetLineStartPosition(-1);
                        if (prev == null || prev.CompareTo(ptr) == 0) break;
                        ptr = prev;
                        line++;
                    }
                    int col = 1;
                    if (ptr != null)
                    {
                        TextPointer start = ptr;
                        TextPointer cur = start;
                        while (cur != null && cur.CompareTo(caret) < 0)
                        {
                            col++;
                            cur = cur.GetNextInsertionPosition(LogicalDirection.Forward);
                        }
                    }
                    CursorPositionText.Text = $"Стр: {line}, Стб: {col}";
                }
                FileInfoText.Text = string.IsNullOrEmpty(currentFilePath) ? "Новый файл" : Path.GetFileName(currentFilePath);
                if (isTextChanged && !FileInfoText.Text.EndsWith("*"))
                    FileInfoText.Text += "*";
            }
            catch { CursorPositionText.Text = "Стр: 1, Стб: 1"; }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!PromptSaveChanges())
                e.Cancel = true;
            base.OnClosing(e);
        }
    }
}