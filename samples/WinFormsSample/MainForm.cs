using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ThaiNationalIDCard.Windows;

namespace ThaiNationalIDCard.WinFormsSample
{
    /// <summary>
    /// Watches every reader and reads a card when it arrives. What to copy from here: events are marshalled to the UI thread,
    /// private mode is an explicit choice for one read, shown data goes away with the card, and nothing is stored or logged.
    /// The form is built in code so that it has no designer files to keep in step. It has only been compiled, never run.
    /// </summary>
    internal sealed class MainForm : Form
    {
        private const int PublicIndex = 0;
        private const int PrivateIndex = 1;
        private const int VisibleDigits = 4;
        private const int CitizenIdLength = 13;

        private readonly ThaiIdCardReader _reader = new ThaiIdCardReader();

        private readonly Button _watchButton = new Button { Text = "Start watching", AutoSize = true };
        private readonly ComboBox _modeBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
        private readonly CheckBox _showCitizenId = new CheckBox { Text = "Show whole citizen ID", AutoSize = true };
        private readonly Label _status = new Label { Text = "Not watching.", Dock = DockStyle.Bottom, Height = 28, TextAlign = ContentAlignment.MiddleLeft };
        private readonly ProgressBar _progress = new ProgressBar { Dock = DockStyle.Bottom, Height = 8 };
        private readonly PictureBox _photo = new PictureBox { Dock = DockStyle.Right, Width = 220, SizeMode = PictureBoxSizeMode.Zoom };
        private readonly ListView _fields = new ListView { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true };

        private ThaiIdCardMonitor? _monitor;
        private CancellationTokenSource? _readCancel;
        private ThaiIdCardData? _shown;

        public MainForm()
        {
            Text = "ThaiNationalIDCard sample";
            ClientSize = new Size(820, 520);

            _modeBox.Items.AddRange(new object[] { "Public (what the card shows)", "Public and private (holder consent needed)" });
            _modeBox.SelectedIndex = PublicIndex;
            _modeBox.SelectedIndexChanged += OnModeChanged;
            _watchButton.Click += OnWatchClicked;
            _showCitizenId.CheckedChanged += (sender, e) => ShowCard();

            _fields.Columns.Add("Field", 170);
            _fields.Columns.Add("Value", 330);
            _fields.Columns.Add("Quality", 80);

            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40, Padding = new Padding(6) };
            top.Controls.AddRange(new Control[] { _watchButton, _modeBox, _showCitizenId });

            // Docking runs from the last control added to the first, so the fill control goes in first.
            Controls.Add(_fields);
            Controls.Add(_photo);
            Controls.Add(top);
            Controls.Add(_progress);
            Controls.Add(_status);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            StopWatching();
            CancelRead();
            ClearCard();
            base.OnFormClosed(e);
        }

        private static string Masked(string? citizenId)
        {
            if (string.IsNullOrEmpty(citizenId))
            {
                return string.Empty;
            }

            int hidden = citizenId.Length == CitizenIdLength ? citizenId.Length - VisibleDigits : citizenId.Length;
            return new string('*', hidden) + citizenId[hidden..];
        }

        private static string FullName(ThaiIdCardName name) =>
            string.Join(" ", new[] { name.Prefix, name.FirstName, name.MiddleName, name.LastName });

        private static string Date(ThaiCardDate date)
        {
            if (date.IsLifetime)
            {
                return "no expiry";
            }

            DateTime? whole = date.ToDateTime();
            return whole?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "unknown or partly unknown: " + date.Raw;
        }

        private static string ByteCount(CardRawValue value) => (value.GetBytes()?.Length ?? 0).ToString(CultureInfo.InvariantCulture) + " bytes";

        /// <summary>Monitor events arrive on a thread pool thread and controls may only be touched on the UI thread.</summary>
        private void PostToUi(Action action)
        {
            if (!IsHandleCreated || IsDisposed)
            {
                return;
            }

            try
            {
                BeginInvoke(action);
            }
            catch (InvalidOperationException)
            {
                // The form closed between the check and the call, so nobody is left to show this to.
            }
        }

        private void OnWatchClicked(object? sender, EventArgs e)
        {
            if (_monitor != null)
            {
                StopWatching();
                CancelRead();
                ClearCard();
                ResetToPublic();
                _status.Text = "Not watching.";
                return;
            }

            ThaiIdCardMonitor? monitor = null;
            try
            {
                IReadOnlyList<string> names = _reader.GetReaderNames();
                if (names.Count == 0)
                {
                    _status.Text = "No card reader was found.";
                    return;
                }

                monitor = new ThaiIdCardMonitor(names);
                monitor.CardInserted += (s, args) => PostToUi(() => _ = ReadCardAsync(args.ReaderName));
                monitor.CardRemoved += (s, args) => PostToUi(OnCardRemoved);
                monitor.MonitorError += (s, args) => PostToUi(() => _status.Text = "A reader stopped being watched: " + args.Failure + ".");
                monitor.Start();

                _monitor = monitor;
                monitor = null;
                _watchButton.Text = "Stop watching";
                _status.Text = "Watching. Insert a card.";
            }
            catch (ThaiIdCardException ex)
            {
                _status.Text = ex.Message;
            }
            finally
            {
                monitor?.Dispose();
            }
        }

        private void StopWatching()
        {
            _monitor?.Dispose();
            _monitor = null;
            _watchButton.Text = "Start watching";
        }

        /// <summary>Consent is about one holder, so choosing private asks first and lasts for one read.</summary>
        private void OnModeChanged(object? sender, EventArgs e)
        {
            if (_modeBox.SelectedIndex != PrivateIndex)
            {
                return;
            }

            DialogResult answer = MessageBox.Show(
                this,
                "Private mode reads areas of the chip that are not printed on the card, and they may hold sensitive data.\n\n" +
                "Has the holder of the next card agreed to this?",
                "Holder consent",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (answer != DialogResult.Yes)
            {
                _modeBox.SelectedIndex = PublicIndex;
            }
        }

        private void ResetToPublic() => _modeBox.SelectedIndex = PublicIndex;

        private void OnCardRemoved()
        {
            CancelRead();
            ClearCard();
            ResetToPublic();
            _status.Text = "Card removed. Insert a card.";
        }

        private void CancelRead()
        {
            _readCancel?.Cancel();
            _readCancel?.Dispose();
            _readCancel = null;
        }

        private async Task ReadCardAsync(string readerName)
        {
            CancelRead();
            var cancel = new CancellationTokenSource();
            _readCancel = cancel;

            var options = new ReadOptions
            {
                Mode = _modeBox.SelectedIndex == PrivateIndex ? CardReadMode.PublicAndPrivate : CardReadMode.Public,
                IncludePhoto = true,

                // Created here on the UI thread, so Progress hands each report back to this thread on its own.
                PhotoProgress = new Progress<PhotoReadProgress>(OnPhotoProgress),
            };

            _status.Text = "Reading...";
            try
            {
                ThaiIdCardData card = await _reader.ReadAsync(readerName, options, cancel.Token);
                if (ReferenceEquals(_readCancel, cancel))
                {
                    _shown = card;
                    ShowPhoto(card);
                    ShowCard();
                    _status.Text = "Read finished. This is what the card returned, and does not prove who the holder is.";
                }
            }
            catch (OperationCanceledException)
            {
                // The card left or another read took over, and whatever did that has already updated the form.
            }
            catch (CardRemovedException)
            {
                _status.Text = "The card was removed during the read.";
            }
            catch (UnsupportedCardException)
            {
                _status.Text = "This card did not answer as a Thai ID card.";
            }
            catch (ThaiIdCardException ex)
            {
                // Library messages are fixed text and hold no card data, so showing one is safe.
                _status.Text = ex.Message;
            }
            finally
            {
                // Consent was for this read. The next card starts from Public again.
                ResetToPublic();
            }
        }

        private void OnPhotoProgress(PhotoReadProgress progress)
        {
            _progress.Maximum = Math.Max(progress.TotalBytes, 1);
            _progress.Value = Math.Min(progress.BytesRead, _progress.Maximum);
        }

        private void ShowPhoto(ThaiIdCardData card)
        {
            ClearPhoto();
            try
            {
                _photo.Image = card.GetPhoto()?.ToImage();
            }
            catch (ArgumentException)
            {
                _status.Text = "The photo could not be shown.";
            }
        }

        private void ShowCard()
        {
            _fields.Items.Clear();
            if (_shown == null)
            {
                return;
            }

            ThaiIdCardData card = _shown;
            ThaiIdCardQuality quality = card.Quality;
            AddField("Citizen ID", _showCitizenId.Checked ? card.CitizenId ?? string.Empty : Masked(card.CitizenId), quality.CitizenId);
            AddField("Thai name", FullName(card.ThaiName), quality.ThaiName);
            AddField("English name", FullName(card.EnglishName), quality.EnglishName);
            AddField("Birth date", Date(card.BirthDate), quality.BirthDate);
            AddField("Sex", card.Sex.ToString(), quality.Sex);
            AddField("Address", card.Address.Full, quality.Address);
            AddField("Issue date", Date(card.IssueDate), quality.IssueDate);
            AddField("Expiry date", Date(card.ExpiryDate), quality.ExpiryDate);
            AddField("Issuer", card.Issuer ?? string.Empty, quality.Issuer);
            AddField("Photo", card.PhotoFailure == PhotoFailureReason.None ? string.Empty : card.PhotoFailure.ToString(), quality.Photo);

            // Private values are never shown here on purpose. A real application shows only what its purpose needs.
            ThaiIdCardPrivateData? extra = card.Private;
            if (extra != null)
            {
                AddField("Laser ID", "(hidden)", extra.LaserIdQuality);
                AddField("MOI 0000-0003", ByteCount(extra.CardVersionRaw), extra.CardVersionRaw.Quality);
                AddField("MOI 00E2-00F5", ByteCount(extra.MoiRaw00E2), extra.MoiRaw00E2.Quality);
                AddField("MOI 0177-0178", ByteCount(extra.MoiRaw0177), extra.MoiRaw0177.Quality);
                AddField("MOI 1619-1626", ByteCount(extra.MoiRaw1619), extra.MoiRaw1619.Quality);
                AddField("NHSO 0004", ByteCount(extra.Nhso.Raw0004), extra.Nhso.Raw0004.Quality);
            }
        }

        private void AddField(string label, string value, FieldQuality quality) =>
            _fields.Items.Add(new ListViewItem(new[] { label, value, quality.ToString() }));

        private void ClearCard()
        {
            _shown = null;
            _fields.Items.Clear();
            _progress.Value = 0;
            ClearPhoto();
        }

        private void ClearPhoto()
        {
            Image? old = _photo.Image;
            _photo.Image = null;
            old?.Dispose();
        }
    }
}
