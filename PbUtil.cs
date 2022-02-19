using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

public class PbExportConfig
{
    public string[] ignore_lines;
    public string[] ignore_proto;

    public static PbExportConfig LoadFromString(string str)
    {
        return JsonConvert.DeserializeObject<PbExportConfig>(str);
    }
}

public class PbUtils
{
    private static bool isCurEnum = false;
    private static List<string> curClsName = new List<string>();

    private static bool is_proto_ignore(string name, PbExportConfig config)
    {
        if (config.ignore_proto != null)
        {
            foreach (var ignore_proto in config.ignore_proto)
            {
                if (name.Contains(ignore_proto))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private enum MessageType
    {
        Message,
        Enum,
    }

    private static List<(string, MessageType)> messageKeywords = new List<(string, MessageType)>
    {
        ("message", MessageType.Message),
        ("enum", MessageType.Enum),
    };

    private class MessageInfo
    {
        public class Member
        {
            public string name;
            public string type;
            public string mapValueType;
            public string comment;
            public bool isArray;
        }

        public string name;

        public List<Member> members = new List<Member>();

        public MessageType type;

        public MessageInfo parent;

        public string comment;
    }

    private class Context
    {
        public List<MessageInfo> all_message = new List<MessageInfo>();
        public Stack<MessageInfo> working_messages = new Stack<MessageInfo>();
        public MessageInfo.Member working_member;
        public List<string> pendingComments = new List<string>();
    }

    public static void GenPbAPI(string protobufDir, string todir, string configPath)
    {
        var configJson = File.ReadAllText(configPath, Encoding.UTF8);
        var config = PbExportConfig.LoadFromString(configJson);
        string[] paths = Directory.GetFiles(protobufDir, "*.proto");

        for (var i = 0; i < paths.Length; i++)
        {
            if (is_proto_ignore(paths[i], config)) continue;

            //当前的proto文件
            var context = new Context();
            var reader = new SymbolReader(File.ReadAllText(paths[i]), new[] { '{', '}', '=', ';', '<', ',', '>' });

            var iter = reader.ReadSymbol().GetEnumerator();
            while (iter.MoveNext())
            {
                var symbol = iter.Current;

                bool found_in_message_keywords = false;

                if (context.working_member == null)
                {
                    foreach (var (name, type) in messageKeywords)
                    {
                        if (symbol == name)
                        {
                            found_in_message_keywords = true;

                            if (!iter.MoveNext())
                            {
                                throw new Exception("message no name");
                            }

                            var messageName = iter.Current;

                            var message = new MessageInfo
                            {
                                name = messageName,
                                type = type,
                            };

                            if (context.pendingComments.Count > 0)
                            {
                                message.comment = string.Join("\n", context.pendingComments);
                                context.pendingComments.Clear();
                            }

                            if (context.working_messages.Count > 0)
                            {
                                var parent = context.working_messages.Peek();
                                message.parent = parent;
                            }

                            context.working_messages.Push(message);

                            //skip {
                            asset_next_symbol(iter, "{");

                            break;
                        }
                    }
                }

                if (found_in_message_keywords) continue;

                if (context.working_messages.Count > 0)
                {
                    var current = context.working_messages.Peek();
                    if (symbol == "}")
                    {
                        context.all_message.Add(current);
                        context.working_messages.Pop();
                    }
                    else if (symbol.StartsWith("//"))
                    {
                        if (current.members.Count > 0)
                        {
                            var last_member = current.members[current.members.Count - 1];
                            last_member.comment = symbol.Replace("//", "--");
                        }
                    }
                    else if (symbol == "repeated")
                    {
                        if (context.working_member == null)
                        {
                            context.working_member = new MessageInfo.Member { isArray = true };
                        }
                        else
                        {
                            context.working_member.isArray = true;
                        }
                    }
                    else
                    {
                        if (context.working_member == null)
                        {
                            context.working_member = new MessageInfo.Member();
                        }

                        if (current.type == MessageType.Message && context.working_member.type == null)
                        {
                            if (symbol == "map")
                            {
                                asset_next_symbol(iter, "<");
                                var key_type = asset_next_symbol(iter);
                                context.working_member.type = get_lua_type(key_type);
                                asset_next_symbol(iter, ",");
                                var value_type = asset_next_symbol(iter);
                                context.working_member.type = get_lua_type(value_type);
                                asset_next_symbol(iter, ">");
                            }
                            else
                            {
                                context.working_member.type = get_lua_type(symbol);
                            }
                        }
                        else if (context.working_member.name == null)
                        {
                            context.working_member.name = symbol;
                        }
                        else
                        {
                            // to =
                            if (iter.Current != "=")
                            {
                                throw new Exception($"error symbol {iter.Current}");
                            }

                            //to member number
                            if (!iter.MoveNext())
                            {
                                throw new Exception("error message member");
                            }

                            //to ;
                            asset_next_symbol(iter, ";");

                            if (context.working_member == null)
                            {
                                throw new Exception("null member");
                            }

                            current.members.Add(context.working_member);

                            context.working_member = null;
                        }
                    }
                }
                else
                {
                    if (symbol.StartsWith("//"))
                    {
                        context.pendingComments.Add(symbol.Replace("//", "--"));
                    }
                }
            }

            iter.Dispose();

            if (context.working_member != null)
            {
                throw new Exception("error working member");
            }

            if (context.working_messages.Count > 0)
            {
                throw new Exception("error working messages");
            }

            var sb = new StringBuilder();

            foreach (var messageInfo in context.all_message)
            {
                if (messageInfo.comment != null)
                {
                    sb.AppendLine(messageInfo.comment);
                }

                sb.AppendLine($"---@class {messageInfo.name}");
                if (messageInfo.type == MessageType.Enum)
                {
                    sb.AppendLine($"local {messageInfo.name} =");
                    sb.AppendLine("{");
                    foreach (var member in messageInfo.members)
                    {
                        sb.Append($"\t{member.name} = \"{member.name}\", ");
                        sb.AppendLine(member.comment ?? "");
                    }

                    sb.AppendLine("}");
                }
                else
                {
                    foreach (var member in messageInfo.members)
                    {
                        var typeStr = member.mapValueType != null
                            ? $"table<{member.type},{member.mapValueType}"
                            : member.type;
                        if (member.isArray)
                        {
                            typeStr = $"{typeStr}[]";
                        }

                        sb.Append($"---@field public {member.name} {typeStr} ");
                        sb.AppendLine(member.comment != null ? $"@{member.comment}" : "");
                    }

                    sb.Append($"local {messageInfo.name} = ").AppendLine("{}");
                }

                sb.AppendLine("");
            }


            var info = new FileInfo(paths[i]);
            string fileName = info.Name.Replace(".proto", ".lua");
            string path = todir + "\\" + fileName;
            File.WriteAllText(path, sb.ToString());
        }
    }

    private static string asset_next_symbol(IEnumerator<string> iter, string must = null)
    {
        if (!iter.MoveNext())
        {
            throw new Exception("un excepted end ");
        }

        if (must != null)
        {
            if (iter.Current != must)
            {
                throw new Exception($"excepted symbol {must} but found {iter.Current}");
            }
        }

        return iter.Current;
    }

    private static string get_lua_type(string fieldType)
    {
        if (fieldType == "int32"
            || fieldType == "int64"
            || fieldType == "float"
            || fieldType == "double"
            || fieldType == "uint32"
            || fieldType == "uint64"
            || fieldType == "sint64"
            || fieldType == "fixed32"
            || fieldType == "fixed64"
            || fieldType == "sfixde32"
            || fieldType == "sfixde64"
           )
        {
            return "number";
        }

        if (fieldType == "bool")
        {
            return "boolean";
        }

        if (fieldType == "bytes")
        {
            return "string";
        }

        return Regex.Replace(fieldType, @"pb_\w+\.", "");
    }

    private static bool is_line_ignore(string str, PbExportConfig config)
    {
        if (config.ignore_lines != null)
        {
            foreach (var line in config.ignore_lines)
            {
                if (str.Contains(line))
                {
                    return true;
                }
            }
        }

        return false;
    }
}