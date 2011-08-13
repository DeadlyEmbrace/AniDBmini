﻿
#region Using Statements

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Xml;
using System.Xml.XPath;

using AniDBmini.Collections;

#endregion Using Statements

namespace AniDBmini
{
    public partial class ImportWindow : Window
    {

        #region Fields

        private BackgroundWorker XmlWorker;
        private string xmlPath;
        private bool closePending, isBackup, isWorking;

        private MylistLocal m_myList;
        private Dispatcher uiDispatcher = Dispatcher.CurrentDispatcher;

        #endregion Fields

        #region Constructor

        public ImportWindow(MylistLocal myList)
        {
            m_myList = myList;
            InitializeComponent();
        }

        #endregion Constructor

        #region Private Methods

        private double formatSize(string size)
        {
            return double.Parse(size.Replace(".", null));
        }

        #endregion

        #region Events

        private void BrowseOnClick(object sender, RoutedEventArgs e)
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
			dlg.Filter = "Xml Files|*.xml|Tar Files|*.tgz;*.tar";

			Nullable<bool> result = dlg.ShowDialog();

            if (result == true)
            {
                importFilePath.Text = dlg.FileName;
                importStart.IsEnabled = true;
            }
        }

        private void StartOnClick(object sender, RoutedEventArgs e)
        {
            xmlPath = importFilePath.Text;
            XmlWorker = new BackgroundWorker();

            XmlWorker.DoWork += new DoWorkEventHandler(DoWork);
            XmlWorker.RunWorkerCompleted += (s, _e) =>
            {
                isWorking = false;
                if (MessageBox.Show("Importing finised!", "Status", MessageBoxButton.OK, MessageBoxImage.Information) == MessageBoxResult.OK)
                {
                    if (isBackup)
                        File.Delete(MylistLocal.dbPath + ".bak");

                    m_myList.Entries.Clear();
                    m_myList.PopulateEntries();

                    this.DialogResult = true;
                }
            };

            if (File.Exists(MylistLocal.dbPath))
                if (MessageBox.Show("A mylist database file already exists.\nDo you wish to overwrite it?", "Confirm",
                                    MessageBoxButton.YesNo, MessageBoxImage.Exclamation) == MessageBoxResult.Yes)
                {
                    m_myList.Close();
                    try
                    {
                        File.Move(MylistLocal.dbPath, MylistLocal.dbPath + ".bak");
                        isBackup = true;
                    }
                    catch (IOException) { }
                    finally { File.Delete(MylistLocal.dbPath); }
                }
                else
                    return;

            XmlWorker.RunWorkerAsync();

            isWorking = true;
            importStart.IsEnabled = false;
        }

        private void DoWork(object sender, DoWorkEventArgs e)
        {
            m_myList.Create();

            List<int> m_groupList = new List<int>();
            List<MylistEntry> m_list = new List<MylistEntry>();

            int totalProcessedFiles = 0,
                totalFiles = int.Parse(new XPathDocument(xmlPath).CreateNavigator().Evaluate("count(//file)").ToString());

            using (XmlReader reader = XmlReader.Create(xmlPath))
            {
                reader.ReadToFollowing("mylist");
                // <anime>
                while (reader.ReadToFollowing("anime"))
                {
                    while (closePending) { Thread.Sleep(500); }

                    MylistEntry entry = new MylistEntry();

                    entry.aid = int.Parse(reader["aid"]);
                    entry.type = reader["type"];
                    entry.year = reader["year"];

                    // <titles>
                    reader.ReadToFollowing("default");
                    entry.title = reader.ReadElementContentAsString();
                    uiDispatcher.BeginInvoke(new Action<string>(str => { importFilePath.Text = str; }), "Importing: " + entry.title);

                    reader.ReadToFollowing("nihongo");
                    entry.nihongo = reader.ReadElementContentAsString().formatNullable();

                    reader.ReadToFollowing("english");
                    entry.english = reader.ReadElementContentAsString().formatNullable();
                    // </titles>

                    // <episodes>
                    if (!reader.ReadToFollowing("episodes"))
                        goto Finish;

                    entry.eps_total = int.Parse(reader["total"]);

                    XmlReader episodesReader = reader.ReadSubtree();
                    // <episode>
                    while (episodesReader.ReadToFollowing("episode"))
                    {
                        while (closePending) { Thread.Sleep(500); }

                        EpisodeEntry episode = new EpisodeEntry();

                        episode.eid = int.Parse(episodesReader["eid"]);
                        if (Regex.IsMatch(episodesReader["epno"].Substring(0, 1), @"\D"))
                            continue;
                        else
                            episode.epno = int.Parse(episodesReader["epno"]);

                        episode.airdate = episodesReader["aired"] == "-" ? null :
                                          ExtensionMethods.DateTimeToUnixTime(DateTime.Parse(episodesReader["aired"],
                                                                                             System.Globalization.CultureInfo.CreateSpecificCulture("en-GB"))).ToString();
                        episode.watched = Convert.ToBoolean(int.Parse(episodesReader["watched"]));

                        // <titles>
                        episodesReader.ReadToDescendant("english");
                        episode.english = episodesReader.ReadElementContentAsString();

                        episodesReader.ReadToFollowing("nihongo");
                        episode.nihongo = episodesReader.ReadElementContentAsString().formatNullable();

                        episodesReader.ReadToFollowing("romaji");
                        episode.romaji = episodesReader.ReadElementContentAsString().formatNullable();
                        // </titles>                        

                        // <files>
                        if (!episodesReader.ReadToFollowing("files"))
                            goto Finish;

                        XmlReader filesReader = episodesReader.ReadSubtree();
                        // <file>
                        while (filesReader.ReadToFollowing("file"))
                        {
                            while (closePending) { Thread.Sleep(500); }

                            FileEntry file = new FileEntry();

                            file.lid = int.Parse(filesReader["lid"]);
                            file.watcheddate = filesReader["watched"] == "-" ? null :
                                               ExtensionMethods.DateTimeToUnixTime(DateTime.Parse(episodesReader["watched"],
                                                                                                  System.Globalization.CultureInfo.CreateSpecificCulture("en-GB"))).ToString();
                            file.watched = file.watcheddate != null;
                            file.generic = episodesReader["generic"] != null;

                            if (!file.generic) // generic entries do not have this information
                            {
                                if (filesReader["gid"] != null)
                                    file.gid = int.Parse(filesReader["gid"]);

                                file.ed2k = filesReader["ed2k"];
                                file.length = int.Parse(filesReader["length"]);
                                file.size = int.Parse(filesReader["size"]);
                                file.source = filesReader["source"].formatNullable();
                                file.acodec = filesReader["acodec"].formatNullable();
                                file.vcodec = filesReader["vcodec"].formatNullable();
                                file.vres = filesReader["vres"].formatNullable();
                                
                                if (file.gid != 0 && !m_groupList.Contains(file.gid))
                                {
                                    // <group_name>
                                    filesReader.ReadToFollowing("group_name");
                                    string group_name = filesReader.ReadElementContentAsString();
                                    // </group_name>

                                    // <group_abbr>
                                    filesReader.ReadToFollowing("group_abbr");
                                    string group_abbr = filesReader.ReadElementContentAsString();
                                    // </group_abbr>

                                    m_myList.InsertGroup(file.gid, group_name, group_abbr);
                                    m_groupList.Add(file.gid);
                                }
                            }

                            entry.size += file.size;

                            episode.Files.Add(file);
                            totalProcessedFiles++;
                            importProgressBar.Dispatcher.BeginInvoke(new Action<double, double>((total, processed) =>
                            {
                                importProgressBar.Value = Math.Ceiling(processed / total * 100);
                            }), totalFiles, totalProcessedFiles);
                        // </file>
                        }
                        // </files>
                        filesReader.Close();
                        entry.Episodes.Add(episode);

                        entry.eps_have++;
                        if (episode.watched)
                            entry.eps_watched++;
                    // </episode>
                    }                    
                    // </episodes>
                    episodesReader.Close();

                Finish:
                    m_myList.InsertMylistEntryFromImport(entry);
                // </anime>
                }
             // </mylist>
            }
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            if (!isWorking && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (!isWorking && e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string filePath = ((string[])e.Data.GetData(DataFormats.FileDrop))[0];
                FileInfo fi = new FileInfo(filePath);

                if (fi.Extension == ".xml" || fi.Extension == ".tgz" || fi.Extension == ".tar")
                {
                    importFilePath.Text = filePath;
                    importStart.IsEnabled = true;
                }

                e.Handled = true;
            }
        }

        private void OnClose(object sender, CancelEventArgs e)
        {
            if (isWorking)
            {
                closePending = true;

                if (MessageBox.Show("Are you sure?\nClosing this window will abort the import process.", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Exclamation) != MessageBoxResult.Yes)
                {
                    closePending = false;
                    e.Cancel = true;
                }
                else if (File.Exists(MylistLocal.dbPath))
                {
                    m_myList.Entries.Clear();

                    File.Delete(MylistLocal.dbPath);

                    if (isBackup)
                        File.Delete(MylistLocal.dbPath + ".bak");
                }
            }
        }

        #endregion Events

    }
}