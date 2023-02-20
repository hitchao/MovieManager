﻿
using Jvedio.Core.Logs;
using Jvedio.Entity;
using Jvedio.Mapper;
using SuperUtils.Framework.ORM.Utils;
using SuperUtils.Framework.ORM.Wrapper;
using SuperUtils.IO;
using SuperUtils.WPF.VieModel;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using static Jvedio.MapperManager;
using static Jvedio.VisualTools.WindowHelper;
using static SuperUtils.Media.ImageHelper;

namespace Jvedio.ViewModel
{
    public class VieModel_Details : ViewModelBase
    {
        private Window_Details windowDetails { get; set; }

        public event EventHandler QueryCompleted;

        private bool loadingLabel { get; set; }

        public VieModel_Details()
        {
            windowDetails = GetWindowByName("Window_Details") as Window_Details;
        }

        private bool _TeenMode = ConfigManager.Settings.TeenMode;

        public bool TeenMode
        {
            get { return _TeenMode; }

            set
            {
                _TeenMode = value;
                RaisePropertyChanged();
            }
        }

        private int _SelectImageIndex = 0;

        public int SelectImageIndex
        {
            get { return _SelectImageIndex; }

            set
            {
                _SelectImageIndex = value;
                RaisePropertyChanged();
            }
        }

        private int _InfoSelectedIndex = (int)ConfigManager.Detail.InfoSelectedIndex;

        public int InfoSelectedIndex
        {
            get { return _InfoSelectedIndex; }

            set
            {
                _InfoSelectedIndex = value;
                if (value == 1 && VideoInfo == null)
                    LoadVideoInfo();
                RaisePropertyChanged();
            }
        }

        private bool _ShowScreenShot = ConfigManager.Detail.ShowScreenShot;

        public bool ShowScreenShot
        {
            get { return _ShowScreenShot; }

            set
            {
                _ShowScreenShot = value;
                RaisePropertyChanged();
            }
        }

        private Video _CurrentVideo;

        public Video CurrentVideo
        {
            get { return _CurrentVideo; }

            set
            {
                _CurrentVideo = value;
                RaisePropertyChanged();
            }
        }

        private VideoInfo _VideoInfo;

        public VideoInfo VideoInfo
        {
            get { return _VideoInfo; }

            set
            {
                _VideoInfo = value;
                RaisePropertyChanged();
            }
        }

        private ObservableCollection<string> labellist;

        public ObservableCollection<string> LabelList
        {
            get { return labellist; }

            set
            {
                labellist = value;
                RaisePropertyChanged();
            }
        }

        private ObservableCollection<ActorInfo> _CurrentActorList = new ObservableCollection<ActorInfo>();

        public ObservableCollection<ActorInfo> CurrentActorList
        {
            get { return _CurrentActorList; }

            set
            {
                _CurrentActorList = value;
                RaisePropertyChanged();
            }
        }

        public ObservableCollection<Video> _ViewAssociationDatas;

        public ObservableCollection<Video> ViewAssociationDatas
        {
            get { return _ViewAssociationDatas; }

            set
            {
                _ViewAssociationDatas = value;
                RaisePropertyChanged();
            }
        }

        private ObservableCollection<string> _CurrentLabelList;

        public ObservableCollection<string> CurrentLabelList
        {
            get { return _CurrentLabelList; }

            set
            {
                _CurrentLabelList = value;
                RaisePropertyChanged();
            }
        }

        private string _LabelText = string.Empty;

        public string LabelText
        {
            get { return _LabelText; }

            set
            {
                _LabelText = value;
                RaisePropertyChanged();
                getLabels();
            }
        }

        public void CleanUp()
        {
            // todo
            //MessengerInstance.Unregister(this);
        }

        public void LoadVideoInfo()
        {
            // todo 分段视频
            VideoInfo = Video.GetMediaInfo(CurrentVideo.Path);
        }

        public void SaveLove()
        {
            metaDataMapper.UpdateFieldById("Grade", CurrentVideo.Grade.ToString(), CurrentVideo.DataID);
        }

        public void Load(long dataID)
        {
            // 释放图片内存
            if (CurrentVideo != null)
            {
                CurrentVideo.SmallImage = null;
                CurrentVideo.BigImage = null;
                for (int i = 0; i < CurrentVideo.PreviewImageList.Count; i++)
                {
                    CurrentVideo.PreviewImageList[i] = null;
                }
            }

            if (CurrentActorList != null)
            {
                for (int i = 0; i < CurrentActorList.Count; i++)
                {
                    CurrentActorList[i].SmallImage = null;
                }
            }

            GC.Collect();

            windowDetails.DataID = dataID;

            // todo 事务下导致阻塞
            metaDataMapper.IncreaseFieldById("ViewCount", dataID); // 访问次数+1
            Video video = videoMapper.SelectVideoByID(dataID);
            Video.setTagStamps(ref video); // 设置标签戳
            Video.handleEmpty(ref video);

            // 设置关联
            HashSet<long> set = associationMapper.GetAssociationDatas(video.DataID);
            video.HasAssociation = set.Count > 0;
            video.AssociationList = set.ToList();
            CurrentVideo = video;

            // 磁力
            List<Magnet> magnets = magnetsMapper.SelectList(new SelectWrapper<Magnet>().Eq("DataID", dataID));
            if (magnets?.Count > 0)
            {
                try
                {
                    CurrentVideo.Magnets = magnets.OrderByDescending(arg => arg.Size)
                        .ThenByDescending(arg => arg.Releasedate)
                        .ThenByDescending(arg => string.Join(" ", arg.Tags).Length).ToList();
                }
                catch (Exception ex)
                {
                    Logger.Error(ex);
                }
            }

            BitmapImage image = BitmapImageFromFile(CurrentVideo.getBigImage());
            if (image == null) image = MetaData.DefaultBigImage;
            CurrentVideo.BigImage = image;
            if (InfoSelectedIndex == 1) LoadVideoInfo();
            QueryCompleted?.Invoke(this, new EventArgs());
        }

        public void LoadViewAssoData()
        {
            if (ViewAssociationDatas == null) ViewAssociationDatas = new ObservableCollection<Video>();
            ViewAssociationDatas.Clear();
            GC.Collect();
            if (CurrentVideo.AssociationList == null || CurrentVideo.AssociationList.Count <= 0) return;
            SelectWrapper<Video> wrapper = Video.InitWrapper();
            wrapper.In("metadata.DataID", CurrentVideo.AssociationList.Select(arg => arg.ToString()));
            wrapper.Select(VieModel_Main.SelectFields);

            string sql = VideoMapper.BASE_SQL;

            sql = wrapper.toSelect(false) + sql + wrapper.toWhere(false);

            List<Dictionary<string, object>> list = metaDataMapper.Select(sql);
            List<Video> videos = metaDataMapper.ToEntity<Video>(list, typeof(Video).GetProperties(), false);

            if (videos == null) return;

            for (int i = 0; i < videos.Count; i++)
            {
                Video video = videos[i];
                if (video == null) continue;
                BitmapImage smallimage = ReadImageFromFile(video.getSmallImage());
                BitmapImage bigimage = ReadImageFromFile(video.getBigImage());
                if (smallimage == null) smallimage = MetaData.DefaultSmallImage;
                if (bigimage == null) bigimage = smallimage;
                video.BigImage = bigimage;
                Video.setTagStamps(ref video); // 设置标签戳
                Video.handleEmpty(ref video); // 设置标题和发行日期

                if (ConfigManager.Settings.AutoGenScreenShot)
                {
                    string path = video.GetScreenShot();
                    if (Directory.Exists(path))
                    {
                        string[] array = FileHelper.TryScanDIr(path, "*.*", System.IO.SearchOption.TopDirectoryOnly);
                        if (array.Length > 0)
                        {
                            Video.SetImage(ref video, array[array.Length / 2]);
                            video.BigImage = null;
                            video.BigImage = video.ViewImage;
                        }
                    }
                }
                App.Current.Dispatcher.Invoke(DispatcherPriority.Background, new LoadViewAssoVideoDelegate(LoadViewAssoVideo), video, i);
            }

            // 清除
            for (int i = ViewAssociationDatas.Count - 1; i > videos.Count - 1; i--)
            {
                ViewAssociationDatas.RemoveAt(i);
            }
        }

        private delegate void LoadViewAssoVideoDelegate(Video video, int idx);

        private void LoadViewAssoVideo(Video video, int idx) => ViewAssociationDatas.Add(video);

        public async void getLabels()
        {
            if (loadingLabel) return;
            loadingLabel = true;
            string like_sql = string.Empty;

            string search = LabelText.ToProperSql().Trim();
            if (!string.IsNullOrEmpty(search))
                like_sql = $" and LabelName like '%{search}%' ";

            List<string> labels = new List<string>();
            string sql = "SELECT LabelName,Count(LabelName) as Count  from metadata_to_label " +
                "JOIN metadata on metadata.DataID=metadata_to_label.DataID " +
                $"where metadata.DBId={ConfigManager.Main.CurrentDBId} and metadata.DataType={0}" + like_sql +
                $" GROUP BY LabelName ORDER BY Count DESC";
            List<Dictionary<string, object>> list = metaDataMapper.Select(sql);
            if (list != null)
            {
                foreach (Dictionary<string, object> item in list)
                {
                    if (!item.ContainsKey("LabelName") || !item.ContainsKey("Count") ||
                        item["LabelName"] == null || item["Count"] == null) continue;
                    string labelName = item["LabelName"].ToString();
                    long.TryParse(item["Count"].ToString(), out long count);
                    labels.Add($"{labelName}({count})");
                }
            }

            CurrentLabelList = new ObservableCollection<string>();
            for (int i = 0; i < labels.Count; i++)
            {
                await App.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new LoadLabelDelegate(LoadLabel), labels[i]);
            }

            loadingLabel = false;
        }

        private delegate void LoadLabelDelegate(string str);

        private void LoadLabel(string str) => CurrentLabelList.Add(str);
    }
}
