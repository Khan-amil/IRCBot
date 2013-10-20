﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Xml.Linq;
using System.Xml;
using System.Reflection;
using System.Net;
using Bot;

namespace IRCBot
{
    public class bot_controller
    {
        public List<bot> bot_instances;
        public DateTime run_time;
        public readonly object outputLock = new object();

        private XmlDocument servers = new XmlDocument();
        private XmlDocument default_servers = new XmlDocument();
        private XmlDocument default_module = new XmlDocument();

        internal readonly object listLock = new object();
        internal readonly object errorlock = new object();

        internal List<string> queue_text;
        internal string cur_dir;
        internal string servers_config_path;

        public bot_controller(string server_config_path)
        {
            bot_instances = new List<bot>();
            cur_dir = Directory.GetCurrentDirectory();
            servers_config_path = server_config_path;
            run_time = DateTime.Now;
            queue_text = new List<string>();
            queue_text.Capacity = 1000;
            queue_text.Clear();

            using (Stream ServersStream = this.GetType().Assembly.GetManifestResourceStream("IRCBot.lib.Config.servers.xml"))
            {
                using (XmlReader servers_reader = XmlReader.Create(ServersStream))
                {
                    default_servers.Load(servers_reader);
                }
            }

            using (Stream ModulesStream = this.GetType().Assembly.GetManifestResourceStream("IRCBot.lib.Config.modules.xml"))
            {
                using (XmlReader module_reader = XmlReader.Create(ModulesStream))
                {
                    default_module.Load(module_reader);
                }
            }
            
            if (File.Exists(servers_config_path))
            {
                check_config(default_servers, servers_config_path);
                servers.Load(servers_config_path);
            }
            else
            {
                default_servers.Save(servers_config_path);
                servers.Load(servers_config_path);
            }
        }

        public string[] list_servers()
        {
            string server = "";
            XmlNodeList xnList = servers.SelectNodes("/server_list/server");
            foreach (XmlNode xn in xnList)
            {
                server += "," + xn["server_name"].InnerText;
            }
            char[] sep = new char[] { ',' };
            string[] server_list = server.Trim(',').Split(sep, StringSplitOptions.RemoveEmptyEntries);
            return server_list;
        }

        public bool init_server(string server_name, bool manual)
        {
            bool server_initiated = false;
            if (File.Exists(servers_config_path))
            {
                BotConfig bot_conf = get_bot_conf(server_name);
                if (bot_conf.auto_connect || manual)
                {
                    bot bot_instance = new bot(this, bot_conf);
                    bot_instance.start_bot();
                    bot_instances.Add(bot_instance);
                    server_initiated = true;
                }
            }
            return server_initiated;
        }

        public bool start_bot(string server_name)
        {
            int index = 0;
            bool server_found = false;
            bool server_started = false;
            foreach (Bot.bot bot in bot_instances)
            {
                if (bot.connected == false && bot.connecting == false && bot.disconnected == true && bot.conf.server.Equals(server_name))
                {
                    server_found = true;
                    break;
                }
                index++;
            }
            if (server_found == true)
            {
                server_started = true;
                bot_instances[index].start_bot();
            }
            return server_started;
        }

        public bool stop_bot(string server_name)
        {
            bool server_terminated = false;
            int index = 0;
            foreach (Bot.bot bot in bot_instances)
            {
                if ((bot.connected == true || bot.connecting == true) && bot.conf.server.Equals(server_name))
                {
                    server_terminated = true;
                    bot.worker.CancelAsync();
                    break;
                }
                index++;
            }
            return server_terminated;
        }

        public bot get_bot_instance(string server_name)
        {
            foreach (Bot.bot bot in bot_instances)
            {
                if (server_name.Equals(bot.conf.server))
                {
                    return bot;
                }
            }
            return null;
        }

        public bool remove_bot_instance(string server_name)
        {
            bool server_found = false;
            int index = 0;
            foreach (Bot.bot bot in bot_instances)
            {
                if (server_name.Equals(bot.conf.server))
                {
                    server_found = true;
                    break;
                }
                else
                {
                    server_found = false;
                    index++;
                }
            }
            if (server_found == true && bot_instances.Count > index)
            {
                bot_instances.RemoveAt(index);
            }
            return server_found;
        }

        public bool bot_connected(string server_name)
        {
            foreach (Bot.bot bot in bot_instances)
            {
                if (bot.connected == true && bot.conf.server.Equals(server_name))
                {
                    return bot.connected;
                }
            }
            return false;
        }

        public string get_module_config_path(string server_name)
        {
            if (server_name != null)
            {
                XmlNodeList xnList = servers.SelectNodes("/server_list/server");
                foreach (XmlNode xn in xnList)
                {
                    if (xn["server_name"].InnerText.Equals(server_name))
                    {
                        return Path.GetDirectoryName(servers_config_path) + Path.DirectorySeparatorChar + xn["module_path"].InnerText;
                    }
                }
                return null;
            }
            else
            {
                return null;
            }
        }

        public XmlNode get_server_xml(string server_name)
        {
            if (server_name != null)
            {
                XmlNodeList xnList = servers.SelectNodes("/server_list/server");
                foreach (XmlNode xn in xnList)
                {
                    if (xn["server_name"].InnerText.Equals(server_name))
                    {
                        return xn;
                    }
                }
                return null;
            }
            else
            {
                return default_servers.SelectSingleNode("/server_list/server");
            }
        }

        public void add_server_xml(XmlNode server_xml)
        {
            XmlNode insertNode = servers.ImportNode(server_xml, true);
            servers.ChildNodes[0].AppendChild(insertNode);
            servers.Save(servers_config_path);
        }

        public void save_server_xml(string server_name, XmlNode server_xml)
        {
            XmlNodeList xnList = servers.SelectNodes("/server_list/server");
            foreach (XmlNode xn in xnList)
            {
                if (xn["server_name"].InnerText.Equals(server_name))
                {
                    xn.ParentNode.ReplaceChild(server_xml, xn);
                }
            }
            servers.Save(servers_config_path);
        }

        public bool delete_server_xml(string server_name)
        {
            bool deleted = false;
            if (server_name != null)
            {
                XmlNodeList xnList = servers.SelectNodes("/server_list/server");
                foreach (XmlNode xn in xnList)
                {
                    if (xn["server_name"].InnerText.Equals(server_name))
                    {
                        delete_module_xml(xn["module_path"].InnerText);
                        xn.ParentNode.RemoveChild(xn);
                        deleted = true;
                        break;
                    }
                }
            }
            return deleted;
        }

        public XmlDocument get_module_xml(string server_name)
        {
            if (server_name != null)
            {
                string module_path = get_module_config_path(server_name);
                XmlDocument modules = new XmlDocument();
                modules.Load(module_path);
                return modules;
            }
            else
            {
                return default_module;
            }
        }

        public void add_module_xml(string server_name, XmlDocument module_xml)
        {
            string module_path = get_module_config_path(server_name);
            Directory.CreateDirectory(Path.GetDirectoryName(module_path));
            module_xml.Save(module_path);
        }

        public void save_module_xml(string server_name, XmlDocument module_xml)
        {
            string module_path = get_module_config_path(server_name);
            module_xml.Save(module_path);
        }

        public void delete_module_xml(string module_path)
        {
            File.Delete(Path.GetDirectoryName(servers_config_path) + Path.DirectorySeparatorChar + module_path);
            deleteEmptyDirectories(Path.GetDirectoryName(servers_config_path));
        }

        public void update_conf()
        {
            foreach (bot bot_instance in bot_instances)
            {
                bot_instance.conf = get_bot_conf(bot_instance.conf.server);
            }
        }

        public void update_conf(string server_name)
        {
            foreach (bot bot_instance in bot_instances)
            {
                if (bot_instance.conf.server.Equals(server_name))
                {
                    bot_instance.conf = get_bot_conf(bot_instance.conf.server);
                    break;
                }
            }
        }

        public void update_conf(string old_server_name, string new_server_name)
        {
            foreach (bot bot_instance in bot_instances)
            {
                if (bot_instance.conf.server.Equals(old_server_name))
                {
                    bot_instance.conf = get_bot_conf(new_server_name);
                    break;
                }
            }
        }

        public BotConfig get_bot_conf(string server_name)
        {
            BotConfig bot_conf = new BotConfig();
            XmlNode xn = get_server_xml(server_name);
            if (xn != null)
            {
                string module_path = Path.GetDirectoryName(servers_config_path) + Path.DirectorySeparatorChar + xn["module_path"].InnerText;
                bot_conf.module_config = new List<List<string>>();
                bot_conf.command_list = new List<List<string>>();
                bot_conf.spam_check = new List<spam_info>();
                bot_conf.name = xn["name"].InnerText;
                bot_conf.nick = xn["nick"].InnerText;
                bot_conf.secondary_nicks = xn["sec_nicks"].InnerText;
                bot_conf.pass = xn["password"].InnerText;
                bot_conf.email = xn["email"].InnerText;
                bot_conf.owner = xn["owner"].InnerText;
                bot_conf.port = Convert.ToInt32(xn["port"].InnerText);
                bot_conf.server = xn["server_name"].InnerText;
                bot_conf.server_address = xn["server_address"].InnerText;
                bot_conf.chans = xn["chan_list"].InnerText;
                bot_conf.chan_blacklist = xn["chan_blacklist"].InnerText;
                bot_conf.ignore_list = xn["ignore_list"].InnerText;
                bot_conf.user_level = Convert.ToInt32(xn["user_level"].InnerText);
                bot_conf.voice_level = Convert.ToInt32(xn["voice_level"].InnerText);
                bot_conf.hop_level = Convert.ToInt32(xn["hop_level"].InnerText);
                bot_conf.op_level = Convert.ToInt32(xn["op_level"].InnerText);
                bot_conf.sop_level = Convert.ToInt32(xn["sop_level"].InnerText);
                bot_conf.founder_level = Convert.ToInt32(xn["founder_level"].InnerText);
                bot_conf.owner_level = Convert.ToInt32(xn["owner_level"].InnerText);
                bot_conf.auto_connect = Convert.ToBoolean(xn["auto_connect"].InnerText);
                bot_conf.command = xn["command_prefix"].InnerText;
                bot_conf.spam_enable = Convert.ToBoolean(xn["spam_enable"].InnerText);
                bot_conf.spam_ignore = xn["spam_ignore"].InnerText;
                bot_conf.spam_count_max = Convert.ToInt32(xn["spam_count"].InnerText);
                bot_conf.spam_threshold = Convert.ToInt32(xn["spam_threshold"].InnerText);
                bot_conf.spam_timeout = Convert.ToInt32(xn["spam_timeout"].InnerText);
                bot_conf.max_message_length = Convert.ToInt32(xn["max_message_length"].InnerText);
                bot_conf.keep_logs = xn["keep_logs"].InnerText;
                if (Directory.Exists(xn["logs_path"].InnerText))
                {
                    bot_conf.logs_path = xn["logs_path"].InnerText;
                }
                else
                {
                    bot_conf.logs_path = cur_dir + Path.DirectorySeparatorChar + "logs";
                }
                bot_conf.default_level = Math.Min(bot_conf.user_level, Math.Min(bot_conf.voice_level, Math.Min(bot_conf.hop_level, Math.Min(bot_conf.op_level, Math.Min(bot_conf.sop_level, Math.Min(bot_conf.founder_level, bot_conf.owner_level)))))) - 1;

                try
                {
                    bot_conf.server_ip = Dns.GetHostAddresses(bot_conf.server_address);
                }
                catch
                {
                    bot_conf.server_ip = null;
                }

                XmlDocument xmlDocModules = new XmlDocument();
                check_config(default_module, module_path);
                xmlDocModules = get_module_xml(server_name);
                XmlNode xnNode = xmlDocModules.SelectSingleNode("/modules");
                XmlNodeList xnList = xnNode.ChildNodes;
                foreach (XmlNode xn_module in xnList)
                {
                    if (xn_module["enabled"].InnerText == "True")
                    {
                        List<string> tmp_list = new List<string>();
                        String module_name = xn_module["name"].InnerText;
                        String class_name = xn_module["class_name"].InnerText;
                        tmp_list.Add(class_name);
                        tmp_list.Add(module_name);
                        tmp_list.Add(xn_module["blacklist"].InnerText);

                        XmlNodeList optionList = xn_module.ChildNodes;
                        foreach (XmlNode option in optionList)
                        {
                            if (option.Name.Equals("commands"))
                            {
                                XmlNodeList Options = option.ChildNodes;
                                foreach (XmlNode options in Options)
                                {
                                    List<string> tmp2_list = new List<string>();
                                    tmp2_list.Add(class_name);
                                    tmp2_list.Add(options["name"].InnerText);
                                    tmp2_list.Add(options["description"].InnerText);
                                    tmp2_list.Add(options["triggers"].InnerText);
                                    tmp2_list.Add(options["syntax"].InnerText);
                                    tmp2_list.Add(options["access_level"].InnerText);
                                    tmp2_list.Add(options["blacklist"].InnerText);
                                    tmp2_list.Add(options["show_help"].InnerText);
                                    tmp2_list.Add(options["spam_check"].InnerText);
                                    bot_conf.command_list.Add(tmp2_list);
                                }
                            }
                            if (option.Name.Equals("options"))
                            {
                                XmlNodeList Options = option.ChildNodes;
                                foreach (XmlNode options in Options)
                                {
                                    switch (options["type"].InnerText)
                                    {
                                        case "textbox":
                                            tmp_list.Add(options["value"].InnerText);
                                            break;
                                        case "checkbox":
                                            tmp_list.Add(options["checked"].InnerText);
                                            break;
                                    }
                                }
                            }
                        }
                        bot_conf.module_config.Add(tmp_list);
                    }
                }
            }
            return bot_conf;
        }

        public void run_command(string server_name, string channel, string command, string[] args)
        {
            bool bot_command = true;
            char[] charSeparator = new char[] { ' ' };
            string type = "channel";
            string msg = "";
            if (!channel.StartsWith("#"))
            {
                type = "query";
            }
            bot bot = get_bot_instance(server_name);
            if (bot != null)
            {
                if (args != null)
                {
                    foreach (string arg in args)
                    {
                        msg += " " + arg;
                    }
                }
                string line = ":" + bot.nick + " PRIVMSG " + channel + " :" + bot.conf.command + command + msg;
                string[] ex = line.Split(charSeparator, 5);
                //Run Enabled Modules
                List<Bot.Modules.Module> tmp_module_list = new List<Bot.Modules.Module>();
                tmp_module_list.AddRange(bot.module_list);
                int module_index = 0;
                foreach (Bot.Modules.Module module in tmp_module_list)
                {
                    module_index = 0;
                    bool module_found = false;
                    foreach (List<string> conf_module in bot.conf.module_config)
                    {
                        if (module.ToString().Equals("Bot.Modules." + conf_module[0]))
                        {
                            module_found = true;
                            break;
                        }
                        module_index++;
                    }
                    if (module_found == true)
                    {
                        module.control(bot, bot.conf, module_index, ex, command, bot.conf.owner_level, bot.nick, channel, bot_command, type);
                    }
                }
            }
        }

        public void send_data(string server_name, string cmd, string param)
        {
            bot bot = get_bot_instance(server_name);
            if (bot != null)
            {
                bot.sendData(cmd, param);
            }
        }

        private XmlDocument temp_xml;
        public void check_config(XmlDocument original, string xml_path)
        {
            temp_xml = new XmlDocument();
            temp_xml.Load(xml_path);
            foreach (XmlNode ChNode in original.ChildNodes)
            {
                CompareLower(ChNode);
            }
            temp_xml.Save(xml_path);
        }

        private void CompareLower(XmlNode NodeName)
        {
            foreach (XmlNode ChlNode in NodeName.ChildNodes)
            {
                if (ChlNode.Name == "#text")
                {
                    continue;
                }

                string Path = CreatePath(ChlNode);

                if (temp_xml.SelectNodes(Path).Count <= 0)
                {
                    XmlNode TempNode = temp_xml.ImportNode(ChlNode, true);
                    temp_xml.SelectSingleNode(Path.Substring(0, Path.LastIndexOf("/"))).AppendChild(TempNode);
                }
                else
                {
                    CompareLower(ChlNode);
                }
                //server_xml.Save(server_xml_path);
            }
        }

        private string CreatePath(XmlNode Node)
        {

            string Path = "/" + Node.Name;

            while (!(Node.ParentNode.Name == "#document"))
            {
                Path = "/" + Node.ParentNode.Name + Path;
                Node = Node.ParentNode;
            }
            Path = "/" + Path;
            return Path;

        }

        private static void deleteEmptyDirectories(string startLocation)
        {
            foreach (var directory in Directory.GetDirectories(startLocation))
            {
                deleteEmptyDirectories(directory);
                if (!Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory, false);
                }
            }
        }

        public string[] get_queue()
        {
            string[] queue;
            lock (listLock)
            {
                queue = queue_text.ToArray();
                queue_text.Clear();
            }
            return queue;
        }

        public void log(string log, bot bot, string channel, string date_stamp, string time_stamp)
        {
            if (bot != null && bot.conf.keep_logs.Equals("True") && !log.Trim().Equals(string.Empty))
            {
                string file_name = "";
                file_name = bot.conf.server + "-" + channel + ".log";
                if (bot.conf.logs_path == "")
                {
                    bot.conf.logs_path = cur_dir + Path.DirectorySeparatorChar + "logs";
                }
                if (Directory.Exists(bot.conf.logs_path))
                {
                    StreamWriter log_file = File.AppendText(bot.conf.logs_path + Path.DirectorySeparatorChar + file_name);
                    log_file.WriteLine("[" + date_stamp + " " + time_stamp + "] " + log);
                    log_file.Close();
                }
                else
                {
                    Directory.CreateDirectory(cur_dir + Path.DirectorySeparatorChar + "logs");
                    StreamWriter log_file = File.AppendText(cur_dir + Path.DirectorySeparatorChar + "logs" + Path.DirectorySeparatorChar + file_name);
                    log_file.WriteLine("[" + date_stamp + " " + time_stamp + "] " + log);
                    log_file.Close();
                }
            }
        }

        public void log_error(Exception ex, string server_name)
        {
            bot bot = get_bot_instance(server_name);
            if (bot != null && bot.conf.keep_logs.Equals("True") && ex != null)
            {
                string errorMessage =
                    "Unhandled Exception:\n\n" +
                    ex.Message + "\n\n" +
                    ex.GetType() +
                    "\n\nStack Trace:\n" +
                    ex.StackTrace;

                string file_name = "";
                file_name = "Errors.log";
                string time_stamp = DateTime.Now.ToString("hh:mm tt");
                string date_stamp = DateTime.Now.ToString("yyyy-MM-dd");
                string cur_dir = Directory.GetCurrentDirectory();

                if (Directory.Exists(cur_dir + Path.DirectorySeparatorChar + "errors"))
                {
                    StreamWriter log_file = File.AppendText(cur_dir + Path.DirectorySeparatorChar + "errors" + Path.DirectorySeparatorChar + file_name);
                    log_file.WriteLine("[" + date_stamp + " " + time_stamp + "] " + errorMessage);
                    log_file.Close();
                }
                else
                {
                    Directory.CreateDirectory(cur_dir + Path.DirectorySeparatorChar + "errors");
                    StreamWriter log_file = File.AppendText(cur_dir + Path.DirectorySeparatorChar + "errors" + Path.DirectorySeparatorChar + file_name);
                    log_file.WriteLine("[" + date_stamp + " " + time_stamp + "] " + errorMessage);
                    log_file.Close();
                }
            }
        }
    }
}
