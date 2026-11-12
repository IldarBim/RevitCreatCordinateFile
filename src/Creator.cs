using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using System.Collections.Generic;
using System.IO.Compression;
using System.Drawing;
using System.Windows.Forms;
// Resolve name collisions between Revit API and Windows Forms.
using Form = System.Windows.Forms.Form;
using TextBox = System.Windows.Forms.TextBox;
using Panel = System.Windows.Forms.Panel;
using Control = System.Windows.Forms.Control;
using Color = System.Drawing.Color;
using System.Web.Script.Serialization;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace TATBIM.ProjectCreator
{
    internal static class BuildInfo { public const string Version = "0.2.10"; }
    public class ModelRule
    {
        public string Code { get; set; }
        public string Folder { get; set; }
        public string Excel { get; set; }
        public string Template { get; set; }
        public int Count { get; set; } = 1;
        public string AdditionalPrefix { get; set; } = "";
        public int AdditionalCount { get; set; } = 0;
    }

    public class Settings
    {
        public string Prefix { get; set; } = "999";
        public string Name { get; set; } = "YYY";
        public string Common { get; set; } = "ALL";
        public string ProjectRoot { get; set; } = "";
        public string WorksetsRoot { get; set; } = "";
        public bool SfCoordinatesApproved { get; set; } = false;
        public List<ModelRule> Models { get; set; } = new List<ModelRule>();
    }

    public class CreatorApplication : IExternalApplication
    {
        public Result OnStartup(UIControlledApplication application)
        {
            const string tabName = "TATBIM";
            try
            {
                try { application.CreateRibbonTab(tabName); }
                catch (Autodesk.Revit.Exceptions.ArgumentException) { }

                RibbonPanel ribbonPanel = application.CreateRibbonPanel(tabName, "Проекты");
                string assemblyPath = typeof(CreatorApplication).Assembly.Location;
                var buttonData = new PushButtonData(
                    "TATBIM_ProjectCreator_Run",
                    "Создать\nпроект",
                    assemblyPath,
                    typeof(CreatorCommand).FullName);
                buttonData.ToolTip = "Создание RVT из RTE, рабочих наборов и центральных моделей";
                ribbonPanel.AddItem(buttonData);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                System.Windows.Forms.MessageBox.Show(
                    "Не удалось создать вкладку TATBIM: " + ex.Message,
                    "TATBIM Project Creator");
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application) { return Result.Succeeded; }
    }

    [Autodesk.Revit.Attributes.Transaction(Autodesk.Revit.Attributes.TransactionMode.Manual)]
    public class CreatorCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData data, ref string message, ElementSet elements)
        {
            try
            {
                using (var dlg = new CreatorForm(data.Application)) dlg.ShowDialog();
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.ToString();
                return Result.Failed;
            }
        }
    }

    public class CreatorForm : Form
    {
        readonly UIApplication app;
        readonly TextBox prefix = new TextBox();
        readonly TextBox name = new TextBox();
        readonly TextBox common = new TextBox();
        readonly TextBox root = new TextBox();
        readonly TextBox worksets = new TextBox();
        readonly CheckBox approved = new CheckBox { Text = "Я проверил координаты и положение модели SF (разрешить создание остальных)", AutoSize = true };
        readonly DataGridView grid = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = false, AllowUserToAddRows = false, RowHeadersVisible = false, BackgroundColor = Color.White, BorderStyle = BorderStyle.None };
        readonly TextBox report = new TextBox { Multiline = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle, BackColor = Color.White };
        readonly BindingSource modelsBinding = new BindingSource();
        readonly Label total = new Label { AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold) };
        readonly string configPath;
        Settings settings;

        public CreatorForm(UIApplication revit)
        {
            app = revit;
            Text = "TATBIM Project Creator v" + BuildInfo.Version + " | Revit 2022";
            Width = 1180;
            Height = 820;
            MinimumSize = new Size(1040, 720);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(245, 247, 250);
            Font = new Font("Segoe UI", 9f, FontStyle.Regular);

            var baseDir = Path.GetDirectoryName(typeof(CreatorForm).Assembly.Location);
            configPath = Path.Combine(baseDir, "settings.json");
            if (!File.Exists(configPath)) throw new FileNotFoundException("Не найден settings.json", configPath);
            settings = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.Deserialize<Settings>(File.ReadAllText(configPath));

            prefix.Text = settings.Prefix;
            name.Text = settings.Name;
            common.Text = settings.Common;
            root.Text = settings.ProjectRoot;
            worksets.Text = settings.WorksetsRoot;
            approved.Checked = settings.SfCoordinatesApproved;

            BuildUi(baseDir);
            UpdateCount();
            Log("TATBIM Project Creator v" + BuildInfo.Version + " | DLL: " + typeof(CreatorForm).Assembly.Location);
            Log("Режим безопасности: SF создаётся отдельно. Перед запуском остальных моделей координаты SF необходимо проверить.");
        }

        void BuildUi(string baseDir)
        {
            Controls.Clear();

            var header = BuildHeader(baseDir);
            var configBox = BuildConfigBox();
            var modelsBox = BuildModelsBox();
            var actionsBox = BuildActionsBox();
            var logBox = BuildLogBox();

            var main = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 4,
                Padding = new Padding(14, 0, 14, 14),
                BackColor = BackColor
            };
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
            main.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 114));
            main.RowStyles.Add(new RowStyle(SizeType.Absolute, 190));

            main.Controls.Add(configBox, 0, 0);
            main.Controls.Add(modelsBox, 0, 1);
            main.Controls.Add(actionsBox, 0, 2);
            main.Controls.Add(logBox, 0, 3);

            Controls.Add(main);
            Controls.Add(header);
        }

        Control BuildHeader(string baseDir)
        {
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 116,
                BackColor = Color.White,
                Padding = new Padding(18, 14, 18, 14)
            };

            var logo = new PictureBox
            {
                Size = new Size(180, 88),
                SizeMode = PictureBoxSizeMode.Zoom,
                Dock = DockStyle.Left
            };
            string logoPath = Path.Combine(baseDir, "tatbim_logo.png");
            if (File.Exists(logoPath))
            {
                try { logo.Image = Image.FromFile(logoPath); }
                catch { }
            }

            var titleWrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18, 8, 0, 0), BackColor = Color.White };
            var title = new Label
            {
                Text = "TATBIM Project Creator  v" + BuildInfo.Version,
                Font = new Font("Segoe UI", 18f, FontStyle.Bold),
                AutoSize = true,
                ForeColor = Color.FromArgb(24, 24, 24),
                Dock = DockStyle.Top
            };
            var subtitle = new Label
            {
                Text = "Создание центральных RVT из RTE, рабочих наборов и координации через SF",
                Font = new Font("Segoe UI", 10f, FontStyle.Regular),
                AutoSize = true,
                ForeColor = Color.FromArgb(90, 96, 105),
                Dock = DockStyle.Top,
                Padding = new Padding(0, 8, 0, 0)
            };
            titleWrap.Controls.Add(subtitle);
            titleWrap.Controls.Add(title);

            header.Controls.Add(titleWrap);
            header.Controls.Add(logo);
            return header;
        }

        Control BuildConfigBox()
        {
            var box = CreateGroupBox("Параметры проекта");

            var outer = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                Padding = new Padding(10, 8, 10, 10)
            };
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

            var row1 = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24));
            row1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28));
            row1.Controls.Add(CreateLabeledInput("Шифр", prefix), 0, 0);
            row1.Controls.Add(CreateLabeledInput("Имя", name), 1, 0);
            row1.Controls.Add(CreateLabeledInput("Общая часть", common), 2, 0);
            row1.Controls.Add(CreateInfoCard("Версия имени файла", "Суффикс: R22"), 3, 0);

            var browseProject = CreateActionButton("Обзор…", 100, 30);
            browseProject.Click += (s, e) => root.Text = PickFolder(root.Text);
            outer.Controls.Add(CreatePathRow("Корневая папка проекта", root, browseProject, "Выбери существующую папку ‘01. Проект’ на локальном диске или сервере"), 0, 1);

            var browseWorksets = CreateActionButton("Обзор…", 100, 30);
            browseWorksets.Click += (s, e) => worksets.Text = PickFolder(worksets.Text);
            outer.Controls.Add(CreatePathRow("Папка Excel с рабочими наборами", worksets, browseWorksets, "Можно использовать сетевую папку с Excel-файлами"), 0, 2);

            outer.Controls.Add(row1, 0, 0);
            box.Controls.Add(outer);
            return box;
        }

        Control BuildModelsBox()
        {
            var box = CreateGroupBox("Состав моделей и шаблоны");
            var wrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 8, 10, 10) };

            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(34, 40, 49);
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.White;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            grid.ColumnHeadersHeight = 34;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(223, 236, 255);
            grid.DefaultCellStyle.SelectionForeColor = Color.Black;
            grid.RowTemplate.Height = 28;
            grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
            grid.GridColor = Color.FromArgb(228, 232, 238);

            grid.Columns.Clear();
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Code", HeaderText = "Код", ReadOnly = true, Width = 70 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Folder", HeaderText = "Папка внутри 01. Проект", ReadOnly = true, Width = 170 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Template", DataPropertyName = "Template", HeaderText = "Путь к RTE", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 180, FillWeight = 180 });
            grid.Columns.Add(new DataGridViewButtonColumn { Name = "BrowseRte", HeaderText = "Файл", Text = "...", UseColumnTextForButtonValue = true, Width = 54 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Excel", DataPropertyName = "Excel", HeaderText = "Excel с рабочими наборами", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, MinimumWidth = 130, FillWeight = 130 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Count", DataPropertyName = "Count", HeaderText = "Кол-во", Width = 80 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "AdditionalPrefix", DataPropertyName = "AdditionalPrefix", HeaderText = "Доп. префикс", Width = 110, ToolTipText = "Отдельная группа, например PP1, PP2. Основная группа BS сохраняется." });
            grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "AdditionalCount", DataPropertyName = "AdditionalCount", HeaderText = "Доп. кол-во", Width = 95, ToolTipText = "0 — дополнительные модели не создавать. Для SF дополнительные модели не предусмотрены." });
            modelsBinding.DataSource = settings.Models;
            grid.DataSource = modelsBinding;
            grid.CellValueChanged += (s, e) => UpdateCount();
            grid.CellContentClick += (s, e) =>
            {
                if (e.RowIndex < 0 || grid.Columns[e.ColumnIndex].Name != "BrowseRte") return;
                if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
                grid.EndEdit();
                using (var picker = new OpenFileDialog())
                {
                    picker.Title = "Выбери шаблон RTE для " + settings.Models[e.RowIndex].Code;
                    picker.Filter = "Шаблоны Revit (*.rte)|*.rte|Все файлы (*.*)|*.*";
                    picker.CheckFileExists = true;
                    picker.RestoreDirectory = true;
                    var current = Convert.ToString(grid.Rows[e.RowIndex].Cells["Template"].Value);
                    if (!string.IsNullOrWhiteSpace(current))
                    {
                        try { string parent = Path.GetDirectoryName(current); if (Directory.Exists(parent)) picker.InitialDirectory = parent; }
                        catch { }
                    }
                    if (picker.ShowDialog(this) == DialogResult.OK)
                    {
                        grid.Rows[e.RowIndex].Cells["Template"].Value = picker.FileName;
                        settings.Models[e.RowIndex].Template = picker.FileName;
                        Log("RTE выбран [" + settings.Models[e.RowIndex].Code + "]: " + picker.FileName);
                    }
                }
            };

            wrap.Controls.Add(grid);
            box.Controls.Add(wrap);
            return box;
        }

        Control BuildActionsBox()
        {
            var box = CreateGroupBox("Действия");
            var row = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 6,
                RowCount = 2,
                Padding = new Padding(10, 7, 10, 5)
            };
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            row.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 160));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var save = CreateActionButton("Сохранить настройки", 150, 34);
            var preview = CreateActionButton("Предпросмотр", 130, 34);
            var sf = CreateActionButton("1. Создать SF", 150, 34);
            var rest = CreateActionButton("2. Создать остальные", 190, 34);
            var links = CreateActionButton("3. Связать RVT", 150, 34);
            var totalWrap = CreateInfoCard("Итого к созданию", "0 моделей");

            save.Click += (s, e) => Save();
            preview.Click += (s, e) => Preview();
            sf.Click += (s, e) => Run(true);
            rest.Click += (s, e) => Run(false);
            links.Click += (s, e) => LinkAllModels();

            row.Controls.Add(save, 0, 0);
            row.Controls.Add(preview, 1, 0);
            row.Controls.Add(sf, 2, 0);
            row.Controls.Add(rest, 3, 0);
            row.Controls.Add(links, 4, 0);
            row.Controls.Add(totalWrap, 5, 0);
            approved.AutoSize = false;
            approved.Dock = DockStyle.Fill;
            approved.AutoEllipsis = false;
            approved.TextAlign = ContentAlignment.MiddleLeft;
            approved.Margin = new Padding(5, 2, 5, 0);
            row.Controls.Add(approved, 0, 1);
            row.SetColumnSpan(approved, 6);

            box.Controls.Add(row);
            total.ParentChanged += (s, e) => UpdateCount();
            return box;
        }

        Control BuildLogBox()
        {
            var box = CreateGroupBox("Журнал выполнения");
            var wrap = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 8, 10, 10) };
            wrap.Controls.Add(report);
            box.Controls.Add(wrap);
            return box;
        }

        GroupBox CreateGroupBox(string title)
        {
            return new GroupBox
            {
                Text = title,
                Dock = DockStyle.Fill,
                Padding = new Padding(8),
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                BackColor = Color.White,
                ForeColor = Color.FromArgb(35, 35, 35)
            };
        }

        Control CreateLabeledInput(string label, TextBox input)
        {
            input.Dock = DockStyle.Fill;
            input.Margin = new Padding(0);
            input.BorderStyle = BorderStyle.FixedSingle;
            input.BackColor = Color.White;
            input.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);

            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Margin = new Padding(5, 0, 5, 0) };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            panel.Controls.Add(new Label { Text = label, AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Regular), ForeColor = Color.FromArgb(70, 70, 70) }, 0, 0);
            panel.Controls.Add(input, 0, 1);
            return panel;
        }

        Control CreatePathRow(string label, TextBox input, Button browseButton, string hint)
        {
            input.Dock = DockStyle.Fill;
            input.BorderStyle = BorderStyle.FixedSingle;
            input.Font = new Font("Segoe UI", 9.5f, FontStyle.Regular);
            input.BackColor = Color.White;

            var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0) };
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));

            var labelWrap = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, Margin = new Padding(0, 3, 10, 0) };
            labelWrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
            labelWrap.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
            labelWrap.Controls.Add(new Label { Text = label, AutoSize = true, Font = new Font("Segoe UI", 9f, FontStyle.Regular), ForeColor = Color.FromArgb(70, 70, 70) }, 0, 0);
            labelWrap.Controls.Add(new Label { Text = hint, AutoSize = true, Font = new Font("Segoe UI", 8f, FontStyle.Italic), ForeColor = Color.FromArgb(120, 120, 120) }, 0, 1);

            row.Controls.Add(labelWrap, 0, 0);
            row.Controls.Add(input, 1, 0);
            row.Controls.Add(browseButton, 2, 0);
            return row;
        }

        Panel CreateInfoCard(string title, string value)
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(247, 248, 250), Margin = new Padding(5, 0, 5, 0), Padding = new Padding(10, 8, 10, 8) };
            var titleLabel = new Label { Text = title, Dock = DockStyle.Top, AutoSize = true, Font = new Font("Segoe UI", 8.5f, FontStyle.Regular), ForeColor = Color.FromArgb(95, 100, 108) };
            var valueLabel = new Label { Text = value, Dock = DockStyle.Top, AutoSize = true, Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = Color.FromArgb(35, 35, 35), Padding = new Padding(0, 6, 0, 0) };
            panel.Controls.Add(valueLabel);
            panel.Controls.Add(titleLabel);
            if (title == "Итого к созданию")
            {
                valueLabel.Name = "TotalCardValue";
                total.Text = value;
                total.Visible = false;
                panel.Controls.Add(total);
            }
            return panel;
        }

        Button CreateActionButton(string text, int width, int height)
        {
            return new Button
            {
                Text = text,
                Width = width,
                Height = height,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.FromArgb(34, 40, 49),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 9f, FontStyle.Bold),
                Margin = new Padding(4, 0, 8, 0)
            };
        }

        void Log(string s)
        {
            report.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + s + Environment.NewLine);
        }

        void UpdateCount()
        {
            int count = settings.Models.Sum(m => Math.Max(0, m.Count) + Math.Max(0, m.AdditionalCount));
            total.Text = "Всего: " + count;
            var totalCardValue = FindControl(this, "TotalCardValue") as Label;
            if (totalCardValue != null) totalCardValue.Text = count + " моделей";
        }

        Control FindControl(Control parent, string name)
        {
            foreach (Control child in parent.Controls)
            {
                if (child.Name == name) return child;
                var nested = FindControl(child, name);
                if (nested != null) return nested;
            }
            return null;
        }

        string PickFolder(string currentPath)
        {
            using (var dialog = new OpenFileDialog())
            {
                dialog.CheckFileExists = false;
                dialog.CheckPathExists = true;
                dialog.ValidateNames = false;
                dialog.FileName = "Выберите папку";
                dialog.Filter = "Папки|*.folder";
                dialog.Title = "Выбор папки";
                dialog.RestoreDirectory = false;

                if (Directory.Exists(currentPath)) dialog.InitialDirectory = currentPath;
                else if (!string.IsNullOrWhiteSpace(currentPath))
                {
                    try
                    {
                        var parent = Path.GetDirectoryName(currentPath);
                        if (Directory.Exists(parent)) dialog.InitialDirectory = parent;
                    }
                    catch { }
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        string selectedPath = Path.GetDirectoryName(dialog.FileName);
                        if (!string.IsNullOrWhiteSpace(selectedPath)) return selectedPath;
                    }
                    catch { }
                }
            }
            return currentPath;
        }

        void ReadForm()
        {
            // Commit the active textbox and BindingSource before reading ModelRule.Template.
            if (grid.IsCurrentCellDirty) grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            if (!grid.EndEdit()) throw new InvalidOperationException("Не удалось сохранить значение ячейки таблицы моделей.");
            modelsBinding.EndEdit();
            // In some WinForms binding configurations the current edit is not yet
            // propagated to the underlying object; copy displayed values explicitly.
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow || row.Index >= settings.Models.Count) continue;
                var rule = settings.Models[row.Index];
                rule.Template = Convert.ToString(row.Cells["Template"].Value ?? "").Trim().Trim('"');
                rule.Excel = Convert.ToString(row.Cells["Excel"].Value ?? "").Trim().Trim('"');
                int count;
                if (!int.TryParse(Convert.ToString(row.Cells["Count"].Value), out count))
                    throw new InvalidOperationException("Количество моделей должно быть целым числом: " + rule.Code);
                rule.Count = count;
                rule.AdditionalPrefix = Convert.ToString(row.Cells["AdditionalPrefix"].Value ?? "").Trim();
                int additionalCount;
                if (!int.TryParse(Convert.ToString(row.Cells["AdditionalCount"].Value), out additionalCount))
                    throw new InvalidOperationException("Дополнительное количество моделей должно быть целым числом: " + rule.Code);
                rule.AdditionalCount = additionalCount;
            }
            settings.Prefix = prefix.Text.Trim();
            settings.Name = name.Text.Trim();
            settings.Common = common.Text.Trim();
            settings.ProjectRoot = root.Text.Trim();
            settings.WorksetsRoot = worksets.Text.Trim();
            settings.SfCoordinatesApproved = approved.Checked;
            var parts = new[] { settings.Prefix, settings.Name, settings.Common };
            if (parts.Any(v => string.IsNullOrWhiteSpace(v) || v.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
                throw new InvalidOperationException("Шифр, имя и общая часть должны быть непустыми и не должны содержать запрещённые символы имени файла.");
            // The project prefix may contain hyphens; Name and Common remain single segments.
            if (settings.Name.Contains("-") || settings.Common.Contains("-"))
                throw new InvalidOperationException("Имя и общая часть не должны содержать '-'. В шифре проекта дефис разрешён.");
            if (!Directory.Exists(settings.ProjectRoot)) throw new DirectoryNotFoundException("Нет корневой папки: " + settings.ProjectRoot);
            if (!Directory.Exists(settings.WorksetsRoot)) throw new DirectoryNotFoundException("Нет папки Excel: " + settings.WorksetsRoot);
            foreach (var m in settings.Models)
            {
                if (m.Count < 0 || m.Count > 99) throw new InvalidOperationException("Допустимое количество 0–99: " + m.Code);
                if (m.AdditionalCount < 0 || m.AdditionalCount > 99)
                    throw new InvalidOperationException("Допустимое дополнительное количество 0–99: " + m.Code);
                if (m.AdditionalCount == 0) continue;
                if (m.Code == "SF") throw new InvalidOperationException("SF — единая модель координации. Дополнительное количество для SF должно быть 0.");
                if (string.IsNullOrWhiteSpace(m.AdditionalPrefix) || m.AdditionalPrefix.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    throw new InvalidOperationException("Укажите дополнительный префикс без запрещённых символов имени файла: " + m.Code);
            }
            var duplicate = PlannedModels(settings.Models)
                .GroupBy(m => Path.GetFullPath(m.Path), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
                throw new InvalidOperationException("Совпадают имена моделей. Измените дополнительный префикс или общую часть: " + duplicate.Key);
            UpdateCount();
        }

        void Save()
        {
            try
            {
                ReadForm();
                File.WriteAllText(configPath, new JavaScriptSerializer().Serialize(settings));
                Log("Настройки сохранены.");
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        string FileName(ModelRule m, int index)
        {
            // Для нескольких моделей одного раздела: BS1, BS2, BS3... вместо ALL.
            // У одиночной модели общая часть (например ALL) не меняется.
            string suffix = index < m.Count
                ? (m.Count > 1 ? "BS" + (index + 1).ToString() : settings.Common)
                : m.AdditionalPrefix + (m.AdditionalCount > 1 ? (index - m.Count + 1).ToString() : "");
            return settings.Prefix + "-" + settings.Name + "-" + m.Code + "-" + suffix + "-R22.rvt";
        }

        string Target(ModelRule m, int index) => Path.Combine(settings.ProjectRoot, m.Folder, FileName(m, index));

        IEnumerable<LinkModel> PlannedModels(IEnumerable<ModelRule> rules)
        {
            // One plan for preview, creation and linking; a single extra has no index.
            return rules.SelectMany(m => Enumerable.Range(0, m.Count + m.AdditionalCount)
                .Select(i => new LinkModel { Rule = m, Code = m.Code, Path = Target(m, i) }));
        }

        void Preview()
        {
            try
            {
                ReadForm();
                report.Clear();
                foreach (var model in PlannedModels(settings.Models))
                    Log(model.Code + " -> " + model.Path + (File.Exists(model.Path) ? " [УЖЕ СУЩЕСТВУЕТ]" : ""));
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        void Run(bool sfOnly)
        {
            try
            {
                ReadForm();
                Log("Проверка RTE перед запуском (v" + BuildInfo.Version + "):");
                foreach (var rule in settings.Models.Where(x => x.Count + x.AdditionalCount > 0 && (sfOnly ? x.Code == "SF" : x.Code != "SF")))
                    Log("  " + rule.Code + ": " + (string.IsNullOrWhiteSpace(rule.Template) ? "[ПУСТО]" : rule.Template) + " | файл доступен: " + File.Exists(rule.Template ?? ""));
                var sfRule = settings.Models.Single(m => m.Code == "SF");
                if (sfRule.Count != 1) throw new InvalidOperationException("Для координации нужна ровно одна SF-модель.");
                string sfPath = Target(sfRule, 0);
                if (!sfOnly && (!approved.Checked || !File.Exists(sfPath)))
                    throw new InvalidOperationException("Сначала создайте SF, задайте и проверьте координаты в SF, затем включите подтверждение.");
                var toRun = PlannedModels(settings.Models.Where(m => sfOnly ? m.Code == "SF" : m.Code != "SF")).ToList();
                int n = toRun.Count, done = 0, good = 0;
                foreach (var model in toRun)
                {
                    var m = model.Rule;
                    done++; string dest = model.Path;
                    try
                    {
                        if (!Directory.Exists(Path.GetDirectoryName(dest))) throw new DirectoryNotFoundException("Папка отсутствует: " + Path.GetDirectoryName(dest));
                        if (File.Exists(dest)) { Log("ПРОПУЩЕН (уже существует): " + dest); continue; }
                        string templatePath = (m.Template ?? "").Trim().Trim('"');
                        Log("RTE [" + m.Code + "]: " + (templatePath.Length == 0 ? "(не задан)" : templatePath));
                        if (templatePath.Length == 0)
                            throw new InvalidOperationException("Для модели " + m.Code + " не указан путь к шаблону RTE. Заполни ячейку и сохрани настройки.");
                        if (!templatePath.EndsWith(".rte", StringComparison.OrdinalIgnoreCase))
                            throw new InvalidOperationException("Для " + m.Code + " выбран файл не формата .rte: " + templatePath);
                        if (!File.Exists(templatePath))
                            throw new FileNotFoundException("Шаблон RTE не найден или недоступен из Revit. Проверь UNC-путь, права доступа к серверу и имя файла. Путь: " + templatePath, templatePath);
                        m.Template = templatePath;
                        var names = string.IsNullOrWhiteSpace(m.Excel) ? new List<string>() : XlsxReader.Names(Path.Combine(settings.WorksetsRoot, m.Excel));
                        if (!sfOnly && names.Count == 0) throw new InvalidOperationException("Нет списка рабочих наборов для " + m.Code + ". Заполни Excel в настройках.");
                        Create(m.Template, dest, names, sfOnly ? null : sfPath);
                        good++; Log("ГОТОВО " + done + "/" + n + "  " + dest);
                    }
                    catch (Exception ex) { Log("ОШИБКА " + done + "/" + n + " [" + m.Code + "]: " + ex.Message); }
                    Application.DoEvents();
                }
                Log("Завершено: " + good + " новых из " + n + " запланированных. Подробности выше.");
                if (!sfOnly && n > 0)
                {
                    Log("Запускаю связывание существующих RVT (неполный комплект допустим)...");
                    LinkAllModelsCore();
                }
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "TATBIM"); }
        }


        void LinkAllModels()
        {
            try { ReadForm(); LinkAllModelsCore(); }
            catch (Exception ex) { Log("ОШИБКА СВЯЗЕЙ: " + ex.Message); MessageBox.Show(ex.Message, "TATBIM — связи RVT"); }
        }

        // Second pass: models must already exist. Never link a model to itself.
        void LinkAllModelsCore()
        {
            var models = PlannedModels(settings.Models).ToList();
            var missing = models.Where(m => !File.Exists(m.Path)).ToList();
            foreach (var item in missing)
                Log("СВЯЗИ: пропущен отсутствующий RVT: " + item.Path);
            models = models.Where(m => File.Exists(m.Path)).ToList();
            Log("СВЯЗИ: найдено " + models.Count + " RVT из " + (models.Count + missing.Count) + " запланированных.");
            if (models.Count < 2)
            {
                Log("СВЯЗИ: для связывания нужны хотя бы две существующие модели.");
                return;
            }
            if (!approved.Checked)
                throw new InvalidOperationException("Подтвердите координаты SF перед загрузкой связей.");
            if (!models.Any(m => m.Code == "SF"))
                throw new InvalidOperationException("Модель SF не найдена. Для безопасной загрузки по общим координатам сначала создайте SF.");
            int complete = 0;
            foreach (var host in models)
            {
                try
                {
                    LinkIntoHost(host, models);
                    complete++;
                    Log("СВЯЗИ: обработана модель " + complete + "/" + models.Count + ": " + Path.GetFileName(host.Path));
                }
                catch (Exception ex)
                {
                    Log("ОШИБКА СВЯЗЕЙ [" + host.Code + "]: " + ex.Message);
                }
                System.Windows.Forms.Application.DoEvents();
            }
            Log("Взаимная загрузка RVT: обработано " + complete + " из " + models.Count + " моделей.");
        }

        sealed class LinkModel
        {
            public ModelRule Rule;
            public string Code;
            public string Path;
        }

        void LinkIntoHost(LinkModel host, List<LinkModel> models)
        {
            string temp = Path.Combine(Path.GetTempPath(), "TATBIM_ProjectCreator_Local_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            string local = Path.Combine(temp, Path.GetFileName(host.Path));
            Document doc = null;
            try
            {
                var centralPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(host.Path);
                var localPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(local);
                WorksharingUtils.CreateNewLocal(centralPath, localPath);
                doc = app.Application.OpenDocumentFile(local);
                if (!doc.IsWorkshared) throw new InvalidOperationException("Файл не поддерживает совместную работу: " + host.Path);
                bool changed = false;
                var already = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (RevitLinkType t in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType)))
                {
                    try
                    {
                        var ext = t.GetExternalFileReference();
                        if (ext != null)
                            already.Add(ModelPathUtils.ConvertModelPathToUserVisiblePath(ext.GetAbsolutePath()));
                    }
                    catch { }
                }
                foreach (var target in models)
                {
                    if (string.Equals(target.Path, host.Path, StringComparison.OrdinalIgnoreCase)) continue;
                    if (already.Contains(target.Path)) { Log("Связь уже существует: " + target.Code + " -> " + host.Code); continue; }
                    WorksetId wsId = EnsureLinkWorkset(doc, target);
                    using (var tx = new Transaction(doc, "TATBIM: связь " + target.Code))
                    {
                        tx.Start();
                        var path = ModelPathUtils.ConvertUserVisiblePathToModelPath(target.Path);
                        var created = RevitLinkType.Create(doc, path, new RevitLinkOptions(false));
                        var instance = RevitLinkInstance.Create(doc, created.ElementId, ImportPlacement.Shared);
                        if (instance == null) throw new InvalidOperationException("Не удалось разместить " + target.Path);
                        var param = instance.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);
                        if (param == null || param.IsReadOnly || !param.Set(wsId.IntegerValue))
                            throw new InvalidOperationException("Невозможно назначить рабочий набор связи " + target.Code);
                        tx.Commit();
                    }
                    already.Add(target.Path);
                    changed = true;
                    Log("Добавлена связь " + Path.GetFileName(target.Path) + " в " +
                        Path.GetFileName(host.Path) + " → " +
                        (new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset)
                            .First(w => w.Id.IntegerValue == wsId.IntegerValue).Name) +
                        " (Shared Coordinates)");
                }
                if (changed)
                {
                    var twc = new TransactWithCentralOptions();
                    var sync = new SynchronizeWithCentralOptions();
                    sync.SetRelinquishOptions(new RelinquishOptions(true));
                    doc.SynchronizeWithCentral(twc, sync);
                }
                // Revit relinquishes all user worksets as a category. Re-checkout
                // the protected 01_Link* worksets afterwards to keep them owned.
                var protectedIds = new FilteredWorksetCollector(doc)
                    .OfKind(WorksetKind.UserWorkset)
                    .Where(w => w.Name.StartsWith("01_Link", StringComparison.OrdinalIgnoreCase))
                    .Select(w => w.Id).ToList();
                if (protectedIds.Count > 0)
                {
                    var ownedIds = WorksharingUtils.CheckoutWorksets(doc, protectedIds);
                    var ownedNumbers = new HashSet<int>(ownedIds.Select(id => id.IntegerValue));
                    foreach (var workset in new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset)
                        .Where(w => w.Name.StartsWith("01_Link", StringComparison.OrdinalIgnoreCase)))
                    {
                        Log((ownedNumbers.Contains(workset.Id.IntegerValue) ? "ВЛАДЕЛЕЦ СОХРАНЁН: " : "НЕ УДАЛОСЬ ЗАКРЕПИТЬ: ") + workset.Name + " в " + host.Code);
                    }
                    // Ensure subsequent central synchronization does not relinquish
                    // the freshly checked-out user worksets.
                    var keep = new RelinquishOptions(true) { UserWorksets = false };
                    var syncKeep = new SynchronizeWithCentralOptions();
                    syncKeep.SetRelinquishOptions(keep);
                    doc.SynchronizeWithCentral(new TransactWithCentralOptions(), syncKeep);
                }
            }
            finally
            {
                if (doc != null && doc.IsValidObject) doc.Close(false);
                // Do not delete temp files if Revit still has a handle open.
                try { Directory.Delete(temp, true); }
                catch { Log("Временная локальная копия осталась в: " + temp); }
            }
        }

        WorksetId EnsureLinkWorkset(Document doc, LinkModel model)
        {
            // Явные соответствия 13 кодов. Запасной "01_Link_<код>" запрещён.
            var mapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "SF", "01_Link_Coordination" },
                { "GP", "01_Link_GP" },
                { "AR", "01_Link_AR" },
                { "KM", "01_Link_KM" },
                { "KR", "01_Link_KG" },
                { "H", "01_Link_H" },
                { "V", "01_Link_V" },
                { "VK", "01_Link_VK" },
                { "SS", "01_Link_SS" },
                { "TX", "01_Link_TX" },
                { "EOM", "01_Link_EOM" },
                { "AYPT", "01_Link_AYPT" },
                { "GS", "01_Link_GS" }
            };
            string baseName;
            if (!mapping.TryGetValue(model.Code, out baseName))
                throw new InvalidOperationException("Нет правила рабочего набора для кода " + model.Code);
            // Индексы BS1, BS2, ... влияют только на имя RVT.
            // Все связи одного раздела используют один существующий набор из mapping.
            // Новые наборы при связывании НЕ создаются.
            string expected = baseName;
            var match = new FilteredWorksetCollector(doc)
                .OfKind(WorksetKind.UserWorkset)
                .FirstOrDefault(w => w.Name.Equals(expected, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                throw new InvalidOperationException("Для " + Path.GetFileName(model.Path) +
                    " отсутствует рабочий набор '" + expected +
                    "'. Связь пропущена; новые рабочие наборы не создаются.");
            return match.Id;
        }

        void Create(string template, string destination, List<string> worksetNames, string sfPath)
        {
            Document doc = null;
            try
            {
                doc = app.Application.NewProjectDocument(template);
                if (doc == null) throw new InvalidOperationException("Revit не создал документ из RTE.");
                // Revit always creates two initial USER worksets when worksharing is enabled.
                // Assign their final names immediately using the Excel lists, so no
                // unwanted "Workset1" / "Shared Levels and Grids" duplicates appear.
                // Only user worksets are handled here; system worksets are unchanged.
                var requested = worksetNames
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Select(n => n.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                if (!doc.IsWorkshared)
                {
                    string sharedName = requested.FirstOrDefault(n =>
                        n.StartsWith("Shared", StringComparison.OrdinalIgnoreCase) ||
                        n.StartsWith("00_Shared", StringComparison.OrdinalIgnoreCase));
                    string linkName = requested.FirstOrDefault(n =>
                        n.StartsWith("LINK", StringComparison.OrdinalIgnoreCase) ||
                        n.StartsWith("01_Link", StringComparison.OrdinalIgnoreCase) ||
                        n.StartsWith("Link", StringComparison.OrdinalIgnoreCase));

                    if (string.IsNullOrEmpty(sharedName) || string.IsNullOrEmpty(linkName) ||
                        string.Equals(sharedName, linkName, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException(
                            "В Excel должны присутствовать два разных рабочих набора: " +
                            "один с префиксом Shared (или 00_Shared), второй с LINK (или 01_Link). " +
                            "Их имена будут использованы для двух обязательных начальных наборов Revit.");
                    doc.EnableWorksharing(sharedName, linkName);
                }

                var existing = new HashSet<string>(
                    new FilteredWorksetCollector(doc).OfKind(WorksetKind.UserWorkset)
                        .Select(w => w.Name), StringComparer.OrdinalIgnoreCase);
                using (var tx = new Transaction(doc, "TATBIM: рабочие наборы"))
                {
                    tx.Start();
                    foreach (string ws in requested)
                        if (existing.Add(ws)) Workset.Create(doc, ws);
                    tx.Commit();
                }
                // Existing worksets from an already-workshared template are preserved:
                // deleting them might also delete/move elements from the RTE.
                // In particular LINK*/Shared* worksets are always preserved.
                Log("Рабочие наборы: " + existing.Count +
                    "; исходные Shared/LINK сохранены; Workset1 не создаётся при новом включении совместной работы.");
                if (sfPath != null)
                {
                    ElementId linkTypeId;
                    using (var tx = new Transaction(doc, "TATBIM: временная связь SF"))
                    {
                        tx.Start();
                        var path = ModelPathUtils.ConvertUserVisiblePathToModelPath(sfPath);
                        var result = RevitLinkType.Create(doc, path, new RevitLinkOptions(false));
                        linkTypeId = result.ElementId;
                        tx.Commit();
                    }
                    ElementId instanceId;
                    using (var tx = new Transaction(doc, "TATBIM: разместить SF"))
                    {
                        tx.Start();
                        RevitLinkInstance instance = RevitLinkInstance.Create(doc, linkTypeId);
                        if (instance == null) throw new InvalidOperationException("Не удалось создать экземпляр связи SF.");
                        instanceId = instance.Id;
                        tx.Commit();
                    }
                    using (var tx = new Transaction(doc, "TATBIM: получить координаты SF"))
                    {
                        tx.Start();
                        doc.AcquireCoordinates(instanceId);
                        tx.Commit();
                    }
                    using (var tx = new Transaction(doc, "TATBIM: убрать временную связь SF"))
                    {
                        tx.Start(); doc.Delete(linkTypeId); tx.Commit();
                    }
                }
                using (var options = new SaveAsOptions { OverwriteExistingFile = false, MaximumBackups = 1 })
                using (var sharing = new WorksharingSaveAsOptions { SaveAsCentral = true })
                {
                    options.SetWorksharingOptions(sharing);
                    doc.SaveAs(destination, options);
                }
            }
            finally { if (doc != null && doc.IsValidObject) doc.Close(false); }
        }
    }

    static class XlsxReader
    {
        public static List<string> Names(string path)
        {
            if (!File.Exists(path)) throw new FileNotFoundException("Excel не найден", path);
            using (var zip = ZipFile.OpenRead(path))
            {
                XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                var strings = new List<string>();
                var shared = zip.GetEntry("xl/sharedStrings.xml");
                if (shared != null)
                {
                    using (var reader = new StreamReader(shared.Open()))
                    {
                        var doc = XDocument.Load(reader);
                        strings = doc.Descendants(ns + "si").Select(si => string.Concat(si.Descendants(ns + "t").Select(t => t.Value))).ToList();
                    }
                }
                var sheet = zip.GetEntry("xl/worksheets/sheet1.xml");
                if (sheet == null) throw new InvalidOperationException("Не найден первый лист Excel: " + path);
                var names = new List<string>();
                using (var reader = new StreamReader(sheet.Open()))
                {
                    var doc = XDocument.Load(reader);
                    foreach (var row in doc.Descendants(ns + "sheetData").Elements(ns + "row"))
                    {
                        var cell = row.Elements(ns + "c").FirstOrDefault(c => ((string)c.Attribute("r") ?? "").StartsWith("A", StringComparison.OrdinalIgnoreCase));
                        if (cell == null) continue;
                        string type = (string)cell.Attribute("t") ?? "";
                        string raw = cell.Element(ns + "v")?.Value ?? "";
                        string value = type == "s" && int.TryParse(raw, out int j) && j < strings.Count ? strings[j]
                            : type == "inlineStr" ? string.Concat(cell.Descendants(ns + "t").Select(x => x.Value)) : raw;
                        value = value.Trim();
                        if (value.Length > 0 && !value.Equals("Рабочие наборы", StringComparison.OrdinalIgnoreCase) && !names.Contains(value)) names.Add(value);
                    }
                }
                return names;
            }
        }
    }
}
