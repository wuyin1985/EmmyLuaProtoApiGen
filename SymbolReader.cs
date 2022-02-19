using System;
using System.Collections.Generic;
using System.Linq;

public class SymbolReader
{
    private string _s;
    private int _length;
    private char[] _spe_keys;

    public SymbolReader(string s, char[] spe_keys)
    {
        _s = s ?? throw new ArgumentNullException(nameof(s));
        _length = s.Length;
        _spe_keys = spe_keys;
    }

    private int find_next_line_end(string s, int start, out int new_pos)
    {
        new_pos = -1;
        for (int i = start; i < s.Length; i++)
        {
            char ch = _s[i];
            new_pos = i;
            if (ch == '\n' || ch == '\r')
            {
                new_pos++;
                if (ch == '\r' && i < s.Length - 1 && s[i + 1] == '\n')
                {
                    new_pos++;
                }

                return i;
            }
        }

        new_pos = s.Length;

        return s.Length - 1;
    }


    public IEnumerable<string> ReadSymbol()
    {
        int last = 0;
        int pos = 0;

        while (pos < _s.Length)
        {
            char ch = _s[pos];
            switch (ch)
            {
                //空格
                case '\t':
                case ' ':
                {
                    if (last != pos)
                    {
                        yield return _s.Substring(last, pos - last);
                    }

                    pos++;
                    last = pos;
                    break;
                }
                //换行
                case '\n':
                case '\r':
                {
                    if (last != pos)
                    {
                        yield return _s.Substring(last, pos - last);
                    }

                    pos++;

                    if (ch == '\r' && pos < _length && _s[pos] == '\n')
                    {
                        pos++;
                    }

                    last = pos;
                    break;
                }
                //注释
                case '/':
                {
                    if (pos < _length - 1 && _s[pos + 1] == '/')
                    {
                        if (pos != last)
                        {
                            yield return _s.Substring(last, pos - last);
                        }

                        var next_line_end = find_next_line_end(_s, pos + 1, out pos);
                        yield return _s.Substring(last, next_line_end - last);
                        last = pos;
                    }
                    else
                    {
                        pos++;
                    }

                    break;
                }
                default:
                {
                    if (_spe_keys.Contains(ch))
                    {
                        if (pos != last)
                        {
                            yield return _s.Substring(last, pos - last);
                            last = pos;
                        }

                        pos++;
                        yield return _s.Substring(last, pos - last);
                        last = pos;
                    }
                    else
                    {
                        pos++;
                    }

                    break;
                }
            }
        }
    }
}