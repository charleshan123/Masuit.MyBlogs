﻿using HtmlAgilityPack;
using IP2Region;
using Masuit.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;

#if !DEBUG
using Masuit.MyBlogs.Core.Models.ViewModel;
using Masuit.Tools.Models;
#endif

namespace Masuit.MyBlogs.Core.Common
{
    /// <summary>
    /// 公共类库
    /// </summary>
    public static class CommonHelper
    {
        static CommonHelper()
        {
            BanRegex = File.ReadAllText(Path.Combine(AppContext.BaseDirectory + "App_Data", "ban.txt"));
            ModRegex = File.ReadAllText(Path.Combine(AppContext.BaseDirectory + "App_Data", "mod.txt"));
            DenyIP = File.ReadAllText(Path.Combine(AppContext.BaseDirectory + "App_Data", "denyip.txt"));
            string[] lines = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory + "App_Data", "DenyIPRange.txt"));
            DenyIPRange = new Dictionary<string, string>();
            foreach (string line in lines)
            {
                try
                {
                    var strs = line.Split(' ');
                    DenyIPRange[strs[0]] = strs[1];
                }
                catch (IndexOutOfRangeException)
                {
                }
            }

            IPWhiteList = File.ReadAllText(Path.Combine(AppContext.BaseDirectory + "App_Data", "whitelist.txt")).Split(',', '，').ToList();
        }

        /// <summary>
        /// 敏感词
        /// </summary>
        public static string BanRegex { get; set; }

        /// <summary>
        /// 审核词
        /// </summary>
        public static string ModRegex { get; set; }

        /// <summary>
        /// 全局禁止IP
        /// </summary>
        public static string DenyIP { get; set; }

        /// <summary>
        /// ip白名单
        /// </summary>
        public static List<string> IPWhiteList { get; set; }

        /// <summary>
        /// 每IP错误的次数统计
        /// </summary>
        public static ConcurrentDictionary<string, int> IPErrorTimes { get; set; } = new ConcurrentDictionary<string, int>();

        /// <summary>
        /// 系统设定
        /// </summary>
        public static ConcurrentDictionary<string, string> SystemSettings { get; set; } = new ConcurrentDictionary<string, string>();

        /// <summary>
        /// 访问量
        /// </summary>
        public static double InterviewCount
        {
            get
            {
                try
                {
                    return RedisHelper.Get<double>("Interview:ViewCount");
                }
                catch
                {
                    return 1;
                }
            }
            set => RedisHelper.IncrBy("Interview:ViewCount");
        }

        /// <summary>
        /// 平均访问量
        /// </summary>
        public static double AverageCount
        {
            get
            {
                try
                {
                    return RedisHelper.Get<double>("Interview:ViewCount") / RedisHelper.Get<double>("Interview:RunningDays");
                }
                catch
                {
                    return 1;
                }
            }
        }

        /// <summary>
        /// 网站启动时间
        /// </summary>
        public static DateTime StartupTime { get; set; } = DateTime.Now;

        /// <summary>
        /// IP黑名单地址段
        /// </summary>
        public static Dictionary<string, string> DenyIPRange { get; set; }

        /// <summary>
        /// 判断IP地址是否被黑名单
        /// </summary>
        /// <param name="ip"></param>
        /// <returns></returns>
        public static bool IsDenyIpAddress(this string ip)
        {
            if (IPWhiteList.Contains(ip))
            {
                return false;
            }

            bool denyed = DenyIP.Split(',').Contains(ip) || DenyIPRange.Any(kv => kv.Key.StartsWith(ip.Split('.')[0]) && ip.IpAddressInRange(kv.Key, kv.Value));
            if (SystemSettings.GetOrAdd("EnableDenyArea", "false") == "true")
            {
                using (DbSearcher searcher = new DbSearcher(Path.Combine(AppContext.BaseDirectory + "App_Data", "ip2region.db")))
                {
                    var pos = searcher.MemorySearch(ip).Region;
                    string[] region = pos.Split("|");
                    string[] denyAreas = SystemSettings.GetOrAdd("DenyArea", "").Split(',', '，');
                    denyed = denyed || denyAreas.Intersect(region).Any() || pos.Contains(denyAreas);
                }
            }

            return denyed;
        }

        /// <summary>
        /// 类型映射
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="source"></param>
        /// <returns></returns>
        public static T Mapper<T>(this object source) where T : class => AutoMapper.Mapper.Map<T>(source);

        /// <summary>
        /// 发送邮件
        /// </summary>
        /// <param name="title">标题</param>
        /// <param name="content">内容</param>
        /// <param name="tos">收件人</param>
        public static void SendMail(string title, string content, string tos)
        {
#if !DEBUG
            new Email()
            {
                EnableSsl = true,
                Body = content,
                SmtpServer = EmailConfig.Smtp,
                Username = EmailConfig.SendFrom,
                Password = EmailConfig.EmailPwd,
                SmtpPort = SystemSettings["SmtpPort"].ToInt32(),
                Subject = title,
                Tos = tos
            }.Send();
#endif
        }

        /// <summary>
        /// 是否是机器人访问
        /// </summary>
        /// <param name="req"></param>
        /// <returns></returns>
        public static bool IsRobot(this HttpRequest req)
        {
            return req.Headers[HeaderNames.UserAgent].ToString().Contains(new[]
            {
                "DNSPod",
                "Baidu",
                "spider",
                "Python",
                "bot"
            });
        }

        /// <summary>
        /// 清理html的img标签的除src之外的其他属性
        /// </summary>
        /// <param name="html"></param>
        /// <returns></returns>
        public static string ClearImgAttributes(this string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var nodes = doc.DocumentNode.Descendants("img");
            foreach (var node in nodes)
            {
                string src = "";
                if (node.Attributes.Contains("data-original"))
                {
                    src = node.Attributes["data-original"].Value;
                }

                if (node.Attributes.Contains("src"))
                {
                    src = node.Attributes["src"].Value;
                }

                node.Attributes.RemoveAll();
                node.Attributes.Add("src", src);
            }

            return doc.DocumentNode.OuterHtml;
        }

        /// <summary>
        /// 将html的img标签的src属性名替换成data-original
        /// </summary>
        /// <param name="html"></param>
        /// <returns></returns>
        public static string ReplaceImgAttribute(this string html)
        {
            var doc = new HtmlDocument();
            doc.LoadHtml(html);
            var nodes = doc.DocumentNode.Descendants("img");
            foreach (var node in nodes)
            {
                if (node.Attributes.Contains("src"))
                {
                    string src = node.Attributes["src"].Value;
                    node.Attributes.Remove("src");
                    node.Attributes.Add("data-original", src);
                }
            }

            return doc.DocumentNode.OuterHtml;
        }
    }
}