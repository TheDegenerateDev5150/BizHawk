using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

using BizHawk.Client.Common;
using BizHawk.Client.EmuHawk.ToolExtensions;
using BizHawk.Common;
using BizHawk.Common.CollectionExtensions;
using BizHawk.Common.StringExtensions;
using BizHawk.Emulation.Common;

namespace BizHawk.Client.EmuHawk
{
	public partial class TAStudio
	{
		private static readonly FilesystemFilterSet MoviesFSFilterSet = new(
			new FilesystemFilter("All Available Files", MovieService.MovieExtensions.Reverse().ToArray()),
			FilesystemFilter.TAStudioProjects,
			FilesystemFilter.BizHawkMovies);

		private void FileSubMenu_DropDownOpened(object sender, EventArgs e)
		{
			SaveBackupMenuItem.Enabled = SaveBk2BackupMenuItem.Enabled = !string.IsNullOrWhiteSpace(CurrentTasMovie.Filename) && CurrentTasMovie.Filename != DefaultTasProjName();
			saveSelectionToMacroToolStripMenuItem.Enabled =
				placeMacroAtSelectionToolStripMenuItem.Enabled =
				recentMacrosToolStripMenuItem.Enabled =
				TasView.AnyRowsSelected;
		}

		private void NewFromSubMenu_DropDownOpened(object sender, EventArgs e)
		{
			NewFromNowMenuItem.Enabled =
				CurrentTasMovie.InputLogLength > 0
				&& !CurrentTasMovie.StartsFromSaveRam;

			NewFromCurrentSaveRamMenuItem.Enabled =
				CurrentTasMovie.InputLogLength > 0
				&& SaveRamEmulator != null;
		}

		private void StartNewProjectFromNowMenuItem_Click(object sender, EventArgs e)
		{
			if (AskSaveChanges())
			{
				var newProject = CurrentTasMovie.ConvertToSavestateAnchoredMovie(
					Emulator.Frame, StatableEmulator.CloneSavestate());

				MainForm.PauseEmulator();
				LoadMovie(newProject, true);
			}
		}

		private void StartANewProjectFromSaveRamMenuItem_Click(object sender, EventArgs e)
		{
			if (AskSaveChanges())
			{
				var saveRam = SaveRamEmulator?.CloneSaveRam(clearDirty: false) ?? throw new Exception("No SaveRam");
				GoToFrame(TasView.AnyRowsSelected ? TasView.FirstSelectedRowIndex : 0);
				var newProject = CurrentTasMovie.ConvertToSaveRamAnchoredMovie(saveRam);
				MainForm.PauseEmulator();
				LoadMovie(newProject, true);
			}
		}

		private void RecentSubMenu_DropDownOpened(object sender, EventArgs e)
			=> RecentSubMenu.ReplaceDropDownItems(Settings.RecentTas.RecentMenu(this, DummyLoadProject, "Project"));

		private void NewTasMenuItem_Click(object sender, EventArgs e) => StartNewTasMovie();

		private void OpenTasMenuItem_Click(object sender, EventArgs e)
		{
			if (!AskSaveChanges()) return;
			var filename = CurrentTasMovie.Filename;
			if (string.IsNullOrWhiteSpace(filename) || filename == DefaultTasProjName())
			{
				filename = "";
			}
			var result = this.ShowFileOpenDialog(
				filter: MoviesFSFilterSet,
				initDir: Config!.PathEntries.MovieAbsolutePath(),
				initFileName: filename);
			if (result is not null) LoadMovieFile(result, askToSave: false);
		}

		/// <summary>
		/// Load the movie with the given filename within TAStudio.
		/// </summary>
		public bool LoadMovieFile(string filename, bool askToSave = true)
		{
			if (askToSave && !AskSaveChanges()) return false;
			if (filename.EndsWithOrdinal(MovieService.TasMovieExtension))
			{
				return LoadFileWithFallback(filename);
			}
			if (filename.EndsWithOrdinal(MovieService.StandardMovieExtension))
			{
				if (!DialogController.ShowMessageBox2(
					caption: "Convert movie",
					icon: EMsgBoxIcon.Question,
					text: "This is a regular movie, a new project must be created from it to use in TAStudio\nProceed?",
					useOKCancel: true))
				{
					return false;
				}

				return LoadFileWithFallback(filename);
			}
			DialogController.ShowMessageBox(
				caption: "Movie load error",
				icon: EMsgBoxIcon.Error,
				text: "This is not a BizHawk movie!");
			return false;
		}

		private void SaveTasMenuItem_Click(object sender, EventArgs e)
		{
			SaveTas();
			if (Settings.BackupPerFileSave)
			{
				SaveTas(saveBackup: true);
			}
		}

		private void SaveAsTasMenuItem_Click(object sender, EventArgs e)
		{
			SaveAsTas();
			if (Settings.BackupPerFileSave)
			{
				SaveTas(saveBackup: true);
			}
		}

		private void SaveBackupMenuItem_Click(object sender, EventArgs e)
		{
			SaveTas(saveBackup: true);
		}

		private void SaveBk2BackupMenuItem_Click(object sender, EventArgs e)
		{
			SaveTas(saveAsBk2: true, saveBackup: true);
		}

		private void SaveSelectionToMacroMenuItem_Click(object sender, EventArgs e)
		{
			if (!TasView.Focused && TasView.AnyRowsSelected)
			{
				return;
			}

			if (TasView.SelectionEndIndex == CurrentTasMovie.InputLogLength)
			{
				TasView.SelectRow(CurrentTasMovie.InputLogLength, false);
			}

			var file = SaveFileDialog(
				null,
				MacroInputTool.SuggestedFolder(Config, Game),
				MacroInputTool.MacrosFSFilterSet,
				this
			);

			if (file != null)
			{
				var selectionStart = TasView.SelectionStartIndex!.Value;
				new MovieZone(
					Emulator,
					Tools,
					MovieSession,
					start: selectionStart,
					length: TasView.SelectionEndIndex!.Value - selectionStart + 1)
					.Save(file.FullName);

				Config.RecentMacros.Add(file.FullName);
			}
		}

		private void PlaceMacroAtSelectionMenuItem_Click(object sender, EventArgs e)
		{
			if (!TasView.Focused && TasView.AnyRowsSelected)
			{
				return;
			}

			var file = OpenFileDialog(
				null,
				MacroInputTool.SuggestedFolder(Config, Game),
				MacroInputTool.MacrosFSFilterSet
			);

			if (file != null)
			{
				DummyLoadMacro(file.FullName);
				Config.RecentMacros.Add(file.FullName);
			}
		}

		private void RecentMacrosMenuItem_DropDownOpened(object sender, EventArgs e)
			=> recentMacrosToolStripMenuItem.ReplaceDropDownItems(Config!.RecentMacros.RecentMenu(this, DummyLoadMacro, "Macro", noAutoload: true));

		private void ToBk2MenuItem_Click(object sender, EventArgs e)
		{
			_autosaveTimer.Stop();

			if (Emulator.HasCycleTiming() && !CurrentTasMovie.IsAtEnd())
			{
				DialogController.ShowMessageBox("This core requires emulation to be on the last frame when writing the movie, otherwise movie length will appear incorrect.", "Warning", EMsgBoxIcon.Warning);
			}

			string filename = CurrentTasMovie.Filename;
			if (string.IsNullOrWhiteSpace(filename) || filename == DefaultTasProjName())
			{
				filename = SuggestedTasProjName();
			}

			filename = Path.ChangeExtension(filename, Bk2Movie.Extension);
			var fileInfo = SaveFileDialog(currentFile: filename, path: Config!.PathEntries.MovieAbsolutePath(), new FilesystemFilterSet(FilesystemFilter.BizHawkMovies), this);

			if (fileInfo is not null)
			{
				MessageStatusLabel.Text = "Exporting to .bk2...";
				MessageStatusLabel.Owner.Update();
				Cursor = Cursors.WaitCursor;
				var bk2 = CurrentTasMovie.ToBk2();
				bk2.Filename = fileInfo.FullName;
				bk2.Attach(Emulator); // required to be able to save the cycle count for ICycleTiming emulators
				bk2.Save();
				MessageStatusLabel.Text = $"{bk2.Name} exported.";
				Cursor = Cursors.Default;
			}
			else
			{
				MessageStatusLabel.Text = "bk2 export cancelled.";
			}

			if (Settings.AutosaveInterval > 0)
			{
				_autosaveTimer.Start();
			}
		}

		private void EditSubMenu_DropDownClosed(object sender, EventArgs e)
		{
			// These specific menu items have their ShortcutKeys property set.
			// These are not user-configurable hotkeys handled by EmuHawk's hotkey system.
			// They are andled by .NET. So they must be enabled in order to work!
			// (We disable them in EditSubMenu_DropDownOpened if there is no selection.)
			CopyMenuItem.Enabled = true;
			CutMenuItem.Enabled = true;
			PasteMenuItem.Enabled = true;
			PasteInsertMenuItem.Enabled = true;
		}

		private void EditSubMenu_DropDownOpened(object sender, EventArgs e)
		{
			DeselectMenuItem.Enabled =
				SelectBetweenMarkersMenuItem.Enabled =
				CopyMenuItem.Enabled =
				CutMenuItem.Enabled =
				ClearFramesMenuItem.Enabled =
				DeleteFramesMenuItem.Enabled =
				CloneFramesMenuItem.Enabled =
				CloneFramesXTimesMenuItem.Enabled =
				TruncateMenuItem.Enabled =
				InsertFrameMenuItem.Enabled =
				InsertNumFramesMenuItem.Enabled =
				TasView.AnyRowsSelected;

			ReselectClipboardMenuItem.Enabled =
				PasteMenuItem.Enabled =
				PasteInsertMenuItem.Enabled = TasView.AnyRowsSelected
				&& (Clipboard.GetDataObject()?.GetDataPresent(DataFormats.StringFormat) ?? false);

			ClearGreenzoneMenuItem.Enabled =
				CurrentTasMovie != null && CurrentTasMovie.TasStateManager.Count > 1;

			GreenzoneICheckSeparator.Visible =
				StateHistoryIntegrityCheckMenuItem.Visible =
				VersionInfo.DeveloperBuild;
		}

		private void UndoMenuItem_Click(object sender, EventArgs e)
		{
			if (CurrentTasMovie.ChangeLog.Undo() < Emulator.Frame)
			{
				GoToFrame(CurrentTasMovie.ChangeLog.PreviousUndoFrame);
			}
			else
			{
				RefreshDialog();
			}

			// Currently I don't have a way to easily detect when CanUndo changes, so this button should be enabled always.
			// UndoMenuItem.Enabled = CurrentTasMovie.ChangeLog.CanUndo;
			RedoMenuItem.Enabled = CurrentTasMovie.ChangeLog.CanRedo;
		}

		private void RedoMenuItem_Click(object sender, EventArgs e)
		{
			if (CurrentTasMovie.ChangeLog.Redo() < Emulator.Frame)
			{
				GoToFrame(CurrentTasMovie.ChangeLog.PreviousRedoFrame);
			}
			else
			{
				RefreshDialog();
			}

			// Currently I don't have a way to easily detect when CanUndo changes, so this button should be enabled always.
			// UndoMenuItem.Enabled = CurrentTasMovie.ChangeLog.CanUndo;
			RedoMenuItem.Enabled = CurrentTasMovie.ChangeLog.CanRedo;
		}

		private void ShowUndoHistoryMenuItem_Click(object sender, EventArgs e)
		{
			_undoForm = new UndoHistoryForm(this) { Owner = this };
			_undoForm.Show();
			_undoForm.UpdateValues();
		}

		private void DeselectMenuItem_Click(object sender, EventArgs e)
		{
			TasView.DeselectAll();
			TasView.Refresh();
		}

		/// <remarks>TODO merge w/ Deselect?</remarks>
		private void SelectAllMenuItem_Click(object sender, EventArgs e)
		{
			TasView.SelectAll();
			TasView.Refresh();
		}

		private void SelectBetweenMarkersMenuItem_Click(object sender, EventArgs e)
		{
			if (TasView.Focused && TasView.AnyRowsSelected)
			{
				var selectionEnd = TasView.SelectionEndIndex ?? 0;
				var prevMarker = CurrentTasMovie.Markers.PreviousOrCurrent(selectionEnd);
				var nextMarker = CurrentTasMovie.Markers.Next(selectionEnd);

				int prev = prevMarker?.Frame ?? 0;
				int next = nextMarker?.Frame ?? CurrentTasMovie.InputLogLength;

				TasView.DeselectAll();
				for (int i = prev; i < next; i++)
				{
					TasView.SelectRow(i, true);
				}

				SetSplicer();
				TasView.Refresh();
			}
		}

		private void ReselectClipboardMenuItem_Click(object sender, EventArgs e)
		{
			TasView.DeselectAll();
			foreach (var item in _tasClipboard)
			{
				TasView.SelectRow(item.Frame, true);
			}

			SetSplicer();
			TasView.Refresh();
		}

		private void CopyMenuItem_Click(object sender, EventArgs e)
		{
			if (TasView.Focused && TasView.AnyRowsSelected)
			{
				_tasClipboard.Clear();
				var list = TasView.SelectedRows.ToArray();
				var sb = new StringBuilder();

				foreach (var index in list)
				{
					var input = CurrentTasMovie.GetInputState(index);
					if (input == null)
					{
						break;
					}

					_tasClipboard.Add(new TasClipboardEntry(index, input));
					var logEntry = Bk2LogEntryGenerator.GenerateLogEntry(input);
					sb.AppendLine(Settings.CopyIncludesFrameNo ? $"{FrameToStringPadded(index)} {logEntry}" : logEntry);
				}

				Clipboard.SetDataObject(sb.ToString());
				SetSplicer();
			}
		}

		private void PasteMenuItem_Click(object sender, EventArgs e)
		{
			if (TasView.Focused && TasView.AnyRowsSelected)
			{
				// TODO: if highlighting 2 rows and pasting 3, only paste 2 of them
				// FCEUX Taseditor doesn't do this, but I think it is the expected behavior in editor programs

				// TODO: copy paste from PasteInsertMenuItem_Click!
				IDataObject data = Clipboard.GetDataObject();
				if (data != null && data.GetDataPresent(DataFormats.StringFormat))
				{
					string input = (string)data.GetData(DataFormats.StringFormat);
					if (!string.IsNullOrWhiteSpace(input))
					{
						string[] lines = input.Split('\n');
						if (lines.Length > 0)
						{
							_tasClipboard.Clear();
							int linesToPaste = lines.Length;
							if (lines[lines.Length - 1].Length is 0) linesToPaste--;
							for (int i = 0; i < linesToPaste; i++)
							{
								var line = ControllerFromMnemonicStr(lines[i]);
								if (line == null)
								{
									return;
								}

								_tasClipboard.Add(new TasClipboardEntry(i, line));
							}

							CurrentTasMovie.CopyOverInput(TasView.SelectionStartIndex ?? 0, _tasClipboard.Select(static x => x.ControllerState));
						}
					}
				}
			}
		}

		private void PasteInsertMenuItem_Click(object sender, EventArgs e)
		{
			if (TasView.Focused && TasView.AnyRowsSelected)
			{
				// copy paste from PasteMenuItem_Click!
				IDataObject data = Clipboard.GetDataObject();
				if (data != null && data.GetDataPresent(DataFormats.StringFormat))
				{
					string input = (string)data.GetData(DataFormats.StringFormat);
					if (!string.IsNullOrWhiteSpace(input))
					{
						string[] lines = input.Split('\n');
						if (lines.Length > 0)
						{
							_tasClipboard.Clear();
							int linesToPaste = lines.Length;
							if (lines[lines.Length - 1].Length is 0) linesToPaste--;
							for (int i = 0; i < linesToPaste; i++)
							{
								var line = ControllerFromMnemonicStr(lines[i]);
								if (line == null)
								{
									return;
								}

								_tasClipboard.Add(new TasClipboardEntry(i, line));
							}

							var selectionStart = TasView.SelectionStartIndex;
							CurrentTasMovie.InsertInput(selectionStart ?? 0, _tasClipboard.Select(static x => x.ControllerState));
						}
					}
				}
			}
		}

		private void CutMenuItem_Click(object sender, EventArgs e)
		{
			if (TasView.Focused && TasView.AnyRowsSelected)
			{

				_tasClipboard.Clear();
				var list = TasView.SelectedRows.ToArray();
				var sb = new StringBuilder();

				foreach (var index in list) // copy of CopyMenuItem_Click()
				{
					var input = CurrentTasMovie.GetInputState(index);
					if (input == null)
					{
						break;
					}

					_tasClipboard.Add(new TasClipboardEntry(index, input));
					sb.AppendLine(Bk2LogEntryGenerator.GenerateLogEntry(input));
				}

				Clipboard.SetDataObject(sb.ToString());
				BeginBatchEdit(); // movie's RemoveFrames may make multiple separate invalidations
				CurrentTasMovie.RemoveFrames(list);
				EndBatchEdit();
				SetSplicer();

			}
		}

		private void ClearFramesMenuItem_Click(object sender, EventArgs e)
		{
			if (TasView.Focused && TasView.AnyRowsSelected)
			{
				BeginBatchEdit();
				CurrentTasMovie.ChangeLog.BeginNewBatch($"Clear frames {TasView.SelectionStartIndex}-{TasView.SelectionEndIndex}");
				foreach (int frame in TasView.SelectedRows)
				{
					CurrentTasMovie.ClearFrame(frame);
				}

				CurrentTasMovie.ChangeLog.EndBatch();
				EndBatchEdit();
			}
		}

		private void DeleteFramesMenuItem_Click(object sender, EventArgs e)
		{
			if (TasView.Focused && TasView.AnyRowsSelected)
			{
				var selectionStart = TasView.SelectionStartIndex;
				var rollBackFrame = selectionStart ?? 0;
				if (rollBackFrame >= CurrentTasMovie.InputLogLength)
				{
					// Cannot delete non-existent frames
					RefreshDialog();
					return;
				}

				BeginBatchEdit(); // movie's RemoveFrames may make multiple separate invalidations
				CurrentTasMovie.RemoveFrames(TasView.SelectedRows.ToArray());
				EndBatchEdit();
				SetTasViewRowCount();
				SetSplicer();
			}
		}

		private void CloneFramesMenuItem_Click(object sender, EventArgs e)
		{
			CloneFramesXTimes(1);
		}

		private void CloneFramesXTimesMenuItem_Click(object sender, EventArgs e)
		{
			using var framesPrompt = new FramesPrompt("Clone # Times", "Insert times to clone:");
			if (framesPrompt.ShowDialogOnScreen().IsOk())
			{
				CloneFramesXTimes(framesPrompt.Frames);
			}
		}

		private void CloneFramesXTimes(int timesToClone)
		{
			BeginBatchEdit();
			for (int i = 0; i < timesToClone; i++)
			{
				if (TasView.Focused && TasView.AnyRowsSelected)
				{
					var framesToInsert = TasView.SelectedRows;
					var insertionFrame = Math.Min((TasView.SelectionEndIndex ?? 0) + 1, CurrentTasMovie.InputLogLength);

					var inputLog = framesToInsert
						.Select(frame => CurrentTasMovie.GetInputLogEntry(frame))
						.ToList();

					CurrentTasMovie.InsertInput(insertionFrame, inputLog);
				}
			}
			EndBatchEdit();
		}

		private void InsertFrameMenuItem_Click(object sender, EventArgs e)
		{
			if (TasView.Focused && TasView.AnyRowsSelected)
			{
				CurrentTasMovie.InsertEmptyFrame(TasView.SelectionStartIndex ?? 0);
			}
		}

		private void InsertNumFramesMenuItem_Click(object sender, EventArgs e)
		{
			if (TasView.Focused && TasView.AnyRowsSelected)
			{
				var insertionFrame = TasView.SelectionStartIndex ?? 0;
				using var framesPrompt = new FramesPrompt();
				if (framesPrompt.ShowDialogOnScreen().IsOk())
				{
					InsertNumFrames(insertionFrame, framesPrompt.Frames);
				}
			}
		}

		private void TruncateMenuItem_Click(object sender, EventArgs e)
		{
			if (TasView.Focused && TasView.AnyRowsSelected)
			{
				CurrentTasMovie.Truncate(TasView.SelectionEndIndex ?? 0);
				MarkerControl.MarkerInputRoll.TruncateSelection(CurrentTasMovie.Markers.Count - 1);
			}
		}

		private void SetMarkersMenuItem_Click(object sender, EventArgs e)
		{
			var selectedRows = TasView.SelectedRows.ToList();
			if (selectedRows.Count > 50)
			{
				var result = DialogController.ShowMessageBox2("Are you sure you want to add more than 50 markers?", "Add markers", EMsgBoxIcon.Question, useOKCancel: true);
				if (!result)
				{
					return;
				}
			}

			foreach (var index in selectedRows)
			{
				MarkerControl.AddMarker(index);
			}
		}

		private void SetMarkerWithTextMenuItem_Click(object sender, EventArgs e)
		{
			MarkerControl.AddMarker(TasView.AnyRowsSelected ? TasView.FirstSelectedRowIndex : 0, true);
		}

		private void RemoveMarkersMenuItem_Click(object sender, EventArgs e)
		{
			CurrentTasMovie.Markers.RemoveAll(m => TasView.IsRowSelected(m.Frame));
			MarkerControl.UpdateMarkerCount();
			RefreshDialog();
		}

		private void ClearGreenzoneMenuItem_Click(object sender, EventArgs e)
		{
			CurrentTasMovie.TasStateManager.Clear();
			RefreshDialog();
		}

		private void StateHistoryIntegrityCheckMenuItem_Click(object sender, EventArgs e)
		{
			if (!Emulator.DeterministicEmulation)
			{
				if (!DialogController.ShowMessageBox2("The emulator is not deterministic. It might fail even if the difference isn't enough to cause a desync.\nContinue with check?", "Not Deterministic"))
				{
					return;
				}
			}

			GoToFrame(0);
			int lastState = 0;
			int goToFrame = CurrentTasMovie.TasStateManager.Last;
			do
			{
				MainForm.FrameAdvance();

				byte[] greenZone = CurrentTasMovie.TasStateManager[Emulator.Frame];
				if (greenZone.Length > 0)
				{
					byte[] state = StatableEmulator.CloneSavestate();

					if (!state.SequenceEqual(greenZone))
					{
						if (DialogController.ShowMessageBox2($"Bad data between frames {lastState} and {Emulator.Frame}. Save the relevant state (raw data)?", "Integrity Failed!"))
						{
							var result = this.ShowFileSaveDialog(initDir: Config!.PathEntries.ToolsAbsolutePath(), initFileName: "integrity.fresh");
							if (result is not null)
							{
								File.WriteAllBytes(result, state);
								var path = Path.ChangeExtension(result, ".greenzoned");
								File.WriteAllBytes(path, greenZone);
							}
						}

						return;
					}

					lastState = Emulator.Frame;
				}
			}
			while (Emulator.Frame < goToFrame);

			DialogController.ShowMessageBox("Integrity Check passed");
		}

		private void ConfigSubMenu_DropDownOpened(object sender, EventArgs e)
		{
			AutopauseAtEndOfMovieMenuItem.Checked = Settings.AutoPause;
			AutosaveAsBk2MenuItem.Checked = Settings.AutosaveAsBk2;
			AutosaveAsBackupFileMenuItem.Checked = Settings.AutosaveAsBackupFile;
			BackupPerFileSaveMenuItem.Checked = Settings.BackupPerFileSave;
			SingleClickAxisEditMenuItem.Checked = Settings.SingleClickAxisEdit;
			OldControlSchemeForBranchesMenuItem.Checked = Settings.OldControlSchemeForBranches;
			LoadBranchOnDoubleclickMenuItem.Checked = Settings.LoadBranchOnDoubleClick;
			BindMarkersToInputMenuItem.Checked = CurrentTasMovie.BindMarkersToInput;
			CopyIncludesFrameNoMenuItem.Checked = Settings.CopyIncludesFrameNo;
		}

		private void SetMaxUndoLevelsMenuItem_Click(object sender, EventArgs e)
		{
			using var prompt = new InputPrompt
			{
				TextInputType = InputPrompt.InputType.Unsigned,
				Message = "Number of Undo Levels to keep",
				InitialValue = CurrentTasMovie.ChangeLog.MaxSteps.ToString(),
			};

			var result = MainForm.DoWithTempMute(() => prompt.ShowDialogOnScreen());
			if (result.IsOk())
			{
				int val = 0;
				try
				{
					val = int.Parse(prompt.PromptText);
				}
				catch
				{
					DialogController.ShowMessageBox("Invalid Entry.", "Input Error", EMsgBoxIcon.Error);
				}

				if (val > 0)
				{
					Settings.MaxUndoSteps = CurrentTasMovie.ChangeLog.MaxSteps = val;
				}
			}
		}
		private void SetRewindStepFastMenuItem_Click(object sender, EventArgs e)
		{
			using var prompt = new InputPrompt
			{
				TextInputType = InputPrompt.InputType.Unsigned,
				Message = "Number of frames to go back\nwhen pressing the rewind key\nwhile fast-forwarding:",
				InitialValue = Settings.RewindStepFast.ToString(),
			};

			var result = MainForm.DoWithTempMute(() => prompt.ShowDialogOnScreen());
			if (!result.IsOk())
			{
				return;
			}

			int val = 0;
			try
			{
				val = int.Parse(prompt.PromptText);
			}
			catch
			{
				DialogController.ShowMessageBox("Invalid Entry.", "Input Error", EMsgBoxIcon.Error);
			}

			if (val > 0)
			{
				Settings.RewindStepFast = val;
			}
		}

		private void SetRewindStepMenuItem_Click(object sender, EventArgs e)
		{
			using var prompt = new InputPrompt
			{
				TextInputType = InputPrompt.InputType.Unsigned,
				Message = "Number of frames to go back\nwhen pressing the rewind key:",
				InitialValue = Settings.RewindStep.ToString(),
			};

			var result = MainForm.DoWithTempMute(() => prompt.ShowDialogOnScreen());
			if (!result.IsOk())
			{
				return;
			}

			int val = 0;
			try
			{
				val = int.Parse(prompt.PromptText);
			}
			catch
			{
				DialogController.ShowMessageBox("Invalid Entry.", "Input Error", EMsgBoxIcon.Error);
			}

			if (val > 0)
			{
				Settings.RewindStep = val;
			}
		}

		private void CopyIncludesFrameNoMenuItem_Click(object sender, EventArgs e)
			=> Settings.CopyIncludesFrameNo = !Settings.CopyIncludesFrameNo;

		private void SetAutosaveIntervalMenuItem_Click(object sender, EventArgs e)
		{
			using var prompt = new InputPrompt
			{
				TextInputType = InputPrompt.InputType.Unsigned,
				Message = "Autosave Interval in seconds\nSet to 0 to disable",
				InitialValue = (Settings.AutosaveInterval / 1000).ToString(),
			};

			var result = MainForm.DoWithTempMute(() => prompt.ShowDialogOnScreen());
			if (result.IsOk())
			{
				uint val = uint.Parse(prompt.PromptText) * 1000;
				Settings.AutosaveInterval = val;
				if (val > 0)
				{
					_autosaveTimer.Interval = (int)val;
					_autosaveTimer.Start();
				}
			}
		}

		private void AutosaveAsBk2MenuItem_Click(object sender, EventArgs e)
			=> Settings.AutosaveAsBk2 = !Settings.AutosaveAsBk2;

		private void AutosaveAsBackupFileMenuItem_Click(object sender, EventArgs e)
			=> Settings.AutosaveAsBackupFile = !Settings.AutosaveAsBackupFile;

		private void BackupPerFileSaveMenuItem_Click(object sender, EventArgs e)
			=> Settings.BackupPerFileSave = !Settings.BackupPerFileSave;

		private void ApplyPatternToPaintedInputMenuItem_CheckedChanged(object sender, EventArgs e)
		{
			onlyOnAutoFireColumnsToolStripMenuItem.Enabled = applyPatternToPaintedInputToolStripMenuItem.Checked;
		}

		private void SingleClickAxisEditMenuItem_Click(object sender, EventArgs e)
			=> Settings.SingleClickAxisEdit = !Settings.SingleClickAxisEdit;

		private void BindMarkersToInputMenuItem_Click(object sender, EventArgs e)
		{
			Settings.BindMarkersToInput = CurrentTasMovie.BindMarkersToInput = BindMarkersToInputMenuItem.Checked;
		}

		private void AutoPauseAtEndMenuItem_Click(object sender, EventArgs e)
			=> Settings.AutoPause = !Settings.AutoPause;

		private void AutoHoldMenuItem_CheckedChanged(object sender, EventArgs e)
		{
			if (autoHoldToolStripMenuItem.Checked)
			{
				autoFireToolStripMenuItem.Checked = false;
				customPatternToolStripMenuItem.Checked = false;

				if (!keepSetPatternsToolStripMenuItem.Checked)
				{
					UpdateAutoFire();
				}
			}
		}

		private void AutoFireMenuItem_CheckedChanged(object sender, EventArgs e)
		{
			if (autoFireToolStripMenuItem.Checked)
			{
				autoHoldToolStripMenuItem.Checked = false;
				customPatternToolStripMenuItem.Checked = false;

				if (!keepSetPatternsToolStripMenuItem.Checked)
				{
					UpdateAutoFire();
				}
			}
		}

		private void CustomPatternMenuItem_CheckedChanged(object sender, EventArgs e)
		{
			if (customPatternToolStripMenuItem.Checked)
			{
				autoHoldToolStripMenuItem.Checked = false;
				autoFireToolStripMenuItem.Checked = false;

				if (!keepSetPatternsToolStripMenuItem.Checked)
				{
					UpdateAutoFire();
				}
			}
		}

		private void SetCustomsMenuItem_Click(object sender, EventArgs e)
		{
			// Exceptions in PatternsForm are not caught by the debugger, I have no idea why.
			// Exceptions in UndoForm are caught, which makes it weirder.
			var pForm = new PatternsForm(this) { Owner = this };
			pForm.Show();
		}

		private void OldControlSchemeForBranchesMenuItem_Click(object sender, EventArgs e)
			=> Settings.OldControlSchemeForBranches = !Settings.OldControlSchemeForBranches;

		private void LoadBranchOnDoubleClickMenuItem_Click(object sender, EventArgs e)
			=> Settings.LoadBranchOnDoubleClick = !Settings.LoadBranchOnDoubleClick;

		private void HeaderMenuItem_Click(object sender, EventArgs e)
		{
			using MovieHeaderEditor form = new(CurrentTasMovie, Config)
			{
				Owner = this,
				Location = this.ChildPointToScreen(TasView),
			};
			form.ShowDialogOnScreen();
		}

		private void StateHistorySettingsMenuItem_Click(object sender, EventArgs e)
		{
			using GreenzoneSettings form = new(
				DialogController,
				new ZwinderStateManagerSettings(CurrentTasMovie.TasStateManager.Settings),
				(s, k) => { CurrentTasMovie.TasStateManager.UpdateSettings(s, k); },
				false)
			{
				Owner = this,
				Location = this.ChildPointToScreen(TasView),
			};
			form.ShowDialogOnScreen();
		}

		private void CommentsMenuItem_Click(object sender, EventArgs e)
		{
			using EditCommentsForm form = new(CurrentTasMovie, false)
			{
				Owner = this,
				StartPosition = FormStartPosition.Manual,
				Location = this.ChildPointToScreen(TasView),
			};
			form.ShowDialogOnScreen();
		}

		private void SubtitlesMenuItem_Click(object sender, EventArgs e)
		{
			using EditSubtitlesForm form = new(
				DialogController,
				CurrentTasMovie,
				Config!.PathEntries,
				readOnly: false)
			{
				Owner = this,
				StartPosition = FormStartPosition.Manual,
				Location = this.ChildPointToScreen(TasView),
			};
			form.ShowDialogOnScreen();
		}

		private void DefaultStateSettingsMenuItem_Click(object sender, EventArgs e)
		{
			using GreenzoneSettings form = new(
				DialogController,
				new ZwinderStateManagerSettings(Config.Movies.DefaultTasStateManagerSettings),
				(s, k) => { Config.Movies.DefaultTasStateManagerSettings = s; },
				true)
			{
				Owner = this,
				Location = this.ChildPointToScreen(TasView),
			};
			form.ShowDialogOnScreen();
		}

		private void SettingsSubMenu_DropDownOpened(object sender, EventArgs e)
		{
			RotateMenuItem.ShortcutKeyDisplayString = TasView.RotateHotkeyStr;
		}

		private void HideLagFramesSubMenu_DropDownOpened(object sender, EventArgs e)
		{
			HideLagFrames0.Checked = TasView.LagFramesToHide == 0;
			HideLagFrames1.Checked = TasView.LagFramesToHide == 1;
			HideLagFrames2.Checked = TasView.LagFramesToHide == 2;
			HideLagFrames3.Checked = TasView.LagFramesToHide == 3;
			hideWasLagFramesToolStripMenuItem.Checked = TasView.HideWasLagFrames;
		}

		private void IconsMenuItem_DropDownOpened(object sender, EventArgs e)
		{
			DenoteStatesWithIconsToolStripMenuItem.Checked = Settings.DenoteStatesWithIcons;
			DenoteStatesWithBGColorToolStripMenuItem.Checked = Settings.DenoteStatesWithBGColor;
			DenoteMarkersWithIconsToolStripMenuItem.Checked = Settings.DenoteMarkersWithIcons;
			DenoteMarkersWithBGColorToolStripMenuItem.Checked = Settings.DenoteMarkersWithBGColor;
		}

		private void FollowCursorMenuItem_DropDownOpened(object sender, EventArgs e)
		{
			alwaysScrollToolStripMenuItem.Checked = Settings.FollowCursorAlwaysScroll;
			scrollToViewToolStripMenuItem.Checked = false;
			scrollToTopToolStripMenuItem.Checked = false;
			scrollToBottomToolStripMenuItem.Checked = false;
			scrollToCenterToolStripMenuItem.Checked = false;
			if (TasView.ScrollMethod == "near")
			{
				scrollToViewToolStripMenuItem.Checked = true;
			}
			else if (TasView.ScrollMethod == "top")
			{
				scrollToTopToolStripMenuItem.Checked = true;
			}
			else if (TasView.ScrollMethod == "bottom")
			{
				scrollToBottomToolStripMenuItem.Checked = true;
			}
			else
			{
				scrollToCenterToolStripMenuItem.Checked = true;
			}
		}

		private void RotateMenuItem_Click(object sender, EventArgs e)
		{
			TasView.HorizontalOrientation = !TasView.HorizontalOrientation;
		}

		private void HideLagFramesX_Click(object sender, EventArgs e)
		{
			TasView.LagFramesToHide = (int)((ToolStripMenuItem)sender).Tag;
			MaybeFollowCursor();
			RefreshDialog();
		}

		private void HideWasLagFramesMenuItem_Click(object sender, EventArgs e)
			=> TasView.HideWasLagFrames = !TasView.HideWasLagFrames;

		private void AlwaysScrollMenuItem_Click(object sender, EventArgs e)
		{
			TasView.AlwaysScroll = Settings.FollowCursorAlwaysScroll = alwaysScrollToolStripMenuItem.Checked;
		}

		private void ScrollToViewMenuItem_Click(object sender, EventArgs e)
		{
			TasView.ScrollMethod = Settings.FollowCursorScrollMethod = "near";
		}

		private void ScrollToTopMenuItem_Click(object sender, EventArgs e)
		{
			TasView.ScrollMethod = Settings.FollowCursorScrollMethod = "top";
		}

		private void ScrollToBottomMenuItem_Click(object sender, EventArgs e)
		{
			TasView.ScrollMethod = Settings.FollowCursorScrollMethod = "bottom";
		}

		private void ScrollToCenterMenuItem_Click(object sender, EventArgs e)
		{
			TasView.ScrollMethod = Settings.FollowCursorScrollMethod = "center";
		}

		private void DenoteStatesWithIconsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			Settings.DenoteStatesWithIcons = DenoteStatesWithIconsToolStripMenuItem.Checked;
			RefreshDialog();
		}

		private void DenoteStatesWithBGColorToolStripMenuItem_Click(object sender, EventArgs e)
		{
			Settings.DenoteStatesWithBGColor = DenoteStatesWithBGColorToolStripMenuItem.Checked;
			RefreshDialog();
		}

		private void DenoteMarkersWithIconsToolStripMenuItem_Click(object sender, EventArgs e)
		{
			Settings.DenoteMarkersWithIcons = DenoteMarkersWithIconsToolStripMenuItem.Checked;
			RefreshDialog();
		}

		private void DenoteMarkersWithBGColorToolStripMenuItem_Click(object sender, EventArgs e)
		{
			Settings.DenoteMarkersWithBGColor = DenoteMarkersWithBGColorToolStripMenuItem.Checked;
			RefreshDialog();
		}

		private void ColorSettingsMenuItem_Click(object sender, EventArgs e)
		{
			using TAStudioColorSettingsForm form = new(Palette, p => Settings.Palette = p)
			{
				Owner = this,
				StartPosition = FormStartPosition.Manual,
				Location = this.ChildPointToScreen(TasView),
			};
			form.ShowDialogOnScreen();
		}

		private void WheelScrollSpeedMenuItem_Click(object sender, EventArgs e)
		{
			var inputPrompt = new InputPrompt
			{
				TextInputType = InputPrompt.InputType.Unsigned,
				Message = "Frames per tick:",
				InitialValue = TasView.ScrollSpeed.ToString(),
			};
			if (!this.ShowDialogWithTempMute(inputPrompt).IsOk()) return;
			TasView.ScrollSpeed = int.Parse(inputPrompt.PromptText);
			Settings.ScrollSpeed = TasView.ScrollSpeed;
		}

		private void SetUpToolStripColumns()
		{
			ColumnsSubMenu.DropDownItems.Clear();

			var columns = TasView.AllColumns
				.Where(static c => !string.IsNullOrWhiteSpace(c.Text) && c.Name is not "FrameColumn")
				.ToList();

			int workingHeight = Screen.FromControl(this).WorkingArea.Height;
			int rowHeight = ColumnsSubMenu.Height + 4;
			int maxRows = workingHeight / rowHeight;
			int keyCount = columns.Count(c => c.Name.StartsWithOrdinal("Key "));
			int keysMenusCount = (int)Math.Ceiling((double)keyCount / maxRows);

			var keysMenus = new ToolStripMenuItem[keysMenusCount];

			for (int i = 0; i < keysMenus.Length; i++)
			{
				keysMenus[i] = new ToolStripMenuItem();
			}

			var playerMenus = new ToolStripMenuItem[Emulator.ControllerDefinition.PlayerCount + 1];
			playerMenus[0] = ColumnsSubMenu;

			for (int i = 1; i < playerMenus.Length; i++)
			{
				playerMenus[i] = new ToolStripMenuItem($"Player {i}");
			}

			foreach (var column in columns)
			{
				var menuItem = new ToolStripMenuItem
				{
					Text = $"{column.Text} ({column.Name})",
					Checked = column.Visible,
					CheckOnClick = true,
					Tag = column.Name,
				};

				menuItem.CheckedChanged += (o, ev) =>
				{
					ToolStripMenuItem sender = (ToolStripMenuItem)o;
					TasView.AllColumns.Find(c => c.Name == (string)sender.Tag).Visible = sender.Checked;
					TasView.AllColumns.ColumnsChanged();
					CurrentTasMovie.FlagChanges();
					TasView.Refresh();
					ColumnsSubMenu.ShowDropDown();
					((ToolStripMenuItem)sender.OwnerItem).ShowDropDown();
				};

				if (column.Name.StartsWithOrdinal("Key "))
				{
					keysMenus
						.First(m => m.DropDownItems.Count < maxRows)
						.DropDownItems
						.Add(menuItem);
				}
				else
				{
					int player;

					if (column.Name.Length >= 2 && column.Name.StartsWith('P') && char.IsNumber(column.Name, 1))
					{
						player = int.Parse(column.Name[1].ToString());
					}
					else
					{
						player = 0;
					}

					playerMenus[player].DropDownItems.Add(menuItem);
				}
			}

			foreach (var menu in keysMenus)
			{
				string text = $"Keys ({menu.DropDownItems[0].Tag} - {menu.DropDownItems[menu.DropDownItems.Count - 1].Tag})";
				menu.Text = text.Replace("Key ", "");
				ColumnsSubMenu.DropDownItems.Add(menu);
			}

			for (int i = 1; i < playerMenus.Length; i++)
			{
				if (playerMenus[i].HasDropDownItems)
				{
					ColumnsSubMenu.DropDownItems.Add(playerMenus[i]);
				}
			}

			ColumnsSubMenu.DropDownItems.Add(new ToolStripSeparator());

			if (keysMenus.Length > 0)
			{
				ToolStripMenuItem item = new("Show Keys") { CheckOnClick = true };
				void UpdateAggregateCheckState()
					=> item.CheckState = keysMenus
						.SelectMany(static submenu => submenu.DropDownItems.OfType<ToolStripMenuItem>())
						.Select(static mi => mi.Checked).Unanimity().ToCheckState();
				UpdateAggregateCheckState();
				var programmaticallyHidingColumns = false;
				EventHandler columnHandler = (_, _) =>
				{
					if (programmaticallyHidingColumns) return;
					programmaticallyHidingColumns = true;
					UpdateAggregateCheckState();
					programmaticallyHidingColumns = false;
				};
				foreach (var submenu in keysMenus) foreach (ToolStripMenuItem button in submenu.DropDownItems)
				{
					button.CheckedChanged += columnHandler;
				}
				item.CheckedChanged += (o, ev) =>
				{
					if (programmaticallyHidingColumns) return;
					programmaticallyHidingColumns = true;
					foreach (var menu in keysMenus)
					{
						foreach (ToolStripMenuItem menuItem in menu.DropDownItems)
						{
							menuItem.Checked = item.Checked;
						}

						CurrentTasMovie.FlagChanges();
						TasView.AllColumns.ColumnsChanged();
						TasView.Refresh();
					}
					programmaticallyHidingColumns = false;
				};
				ColumnsSubMenu.DropDownItems.Add(item);
			}

			for (int i = 1; i < playerMenus.Length; i++)
			{
				ToolStripMenuItem dummyObject = playerMenus[i];
				if (!dummyObject.HasDropDownItems) continue;
				ToolStripMenuItem item = new($"Show Player {i}") { CheckOnClick = true };
				void UpdateAggregateCheckState()
					=> item.CheckState = dummyObject.DropDownItems.OfType<ToolStripMenuItem>().Skip(1)
						.Select(static mi => mi.Checked).Unanimity().ToCheckState();
				UpdateAggregateCheckState();
				var programmaticallyHidingColumns = false;
				EventHandler columnHandler = (_, _) =>
				{
					if (programmaticallyHidingColumns) return;
					programmaticallyHidingColumns = true;
					UpdateAggregateCheckState();
					programmaticallyHidingColumns = false;
				};
				foreach (ToolStripMenuItem button in dummyObject.DropDownItems)
				{
					button.CheckedChanged += columnHandler;
				}
				item.CheckedChanged += (o, ev) =>
				{
					if (programmaticallyHidingColumns) return;
					programmaticallyHidingColumns = true;
					// TODO: preserve underlying button checked state and make this a master visibility control
					foreach (var menuItem in dummyObject.DropDownItems.OfType<ToolStripMenuItem>().Skip(1))
					{
						menuItem.Checked = item.Checked;
					}
					programmaticallyHidingColumns = false;

					CurrentTasMovie.FlagChanges();
					TasView.AllColumns.ColumnsChanged();
					TasView.Refresh();
				};

				dummyObject.DropDownItems.Insert(0, item);
				dummyObject.DropDownItems.Insert(1, new ToolStripSeparator());
			}

			ColumnsSubMenu.DropDownItems.Add(new ToolStripMenuItem
			{
				Enabled = false,
				Text = "Change Peripherals...",
				ToolTipText = "Changing peripherals/players is done in the core's sync settings (if the core supports different peripherals)."
					+ "\nAs these can't be changed in the middle of a movie, you'll have to close TAStudio, change the settings, and create a new TAStudio project.",
			});
		}

		// ReSharper disable once UnusedMember.Local
		[RestoreDefaults]
		private void RestoreDefaults()
		{
			SetUpColumns();
			SetUpToolStripColumns();
			TasView.Refresh();
			CurrentTasMovie.FlagChanges();

			MainVertialSplit.SplitterDistance = _defaultMainSplitDistance;
			BranchesMarkersSplit.SplitterDistance = _defaultBranchMarkerSplitDistance;
		}

		private void RightClickMenu_Opened(object sender, EventArgs e)
		{
			SetMarkersContextMenuItem.Enabled =
				SelectBetweenMarkersContextMenuItem.Enabled =
				RemoveMarkersContextMenuItem.Enabled =
				DeselectContextMenuItem.Enabled =
				ClearContextMenuItem.Enabled =
				DeleteFramesContextMenuItem.Enabled =
				CloneContextMenuItem.Enabled =
				CloneXTimesContextMenuItem.Enabled =
				InsertFrameContextMenuItem.Enabled =
				InsertNumFramesContextMenuItem.Enabled =
				TruncateContextMenuItem.Enabled =
				TasView.AnyRowsSelected;

			pasteToolStripMenuItem.Enabled =
				pasteInsertToolStripMenuItem.Enabled =
				(Clipboard.GetDataObject()?.GetDataPresent(DataFormats.StringFormat) ?? false)
				&& TasView.AnyRowsSelected;

			var selectionIsSingleRow = TasView.SelectedRows.CountIsExactly(1);
			StartNewProjectFromNowMenuItem.Visible =
				selectionIsSingleRow
				&& TasView.IsRowSelected(Emulator.Frame)
				&& !CurrentTasMovie.StartsFromSaveRam;

			StartANewProjectFromSaveRamMenuItem.Visible =
				selectionIsSingleRow
				&& SaveRamEmulator != null
				&& !CurrentTasMovie.StartsFromSavestate;

			StartFromNowSeparator.Visible = StartNewProjectFromNowMenuItem.Visible || StartANewProjectFromSaveRamMenuItem.Visible;
			RemoveMarkersContextMenuItem.Enabled = CurrentTasMovie.Markers.Any(m => TasView.IsRowSelected(m.Frame)); // Disable the option to remove markers if no markers are selected (FCEUX does this).
			CancelSeekContextMenuItem.Enabled = _seekingTo != -1;
			BranchContextMenuItem.Visible = TasView.CurrentCell?.RowIndex == Emulator.Frame;
		}

		private void CancelSeekContextMenuItem_Click(object sender, EventArgs e)
		{
			CancelSeek();
		}

		private void BranchContextMenuItem_Click(object sender, EventArgs e)
		{
			BookMarkControl.Branch();
		}

		private void TASEditorManualOnlineMenuItem_Click(object sender, EventArgs e)
		{
			Util.OpenUrlExternal("http://www.fceux.com/web/help/taseditor/");
		}

		private void ForumThreadMenuItem_Click(object sender, EventArgs e)
		{
			Util.OpenUrlExternal("https://tasvideos.org/Forum/Topics/13505");
		}
	}
}
