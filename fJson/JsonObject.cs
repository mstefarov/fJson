// Part of fCraft | Copyright (c) 2009-2012 Matvei Stefarov <me@matvei.org> | BSD-3 | See LICENSE.txt
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using JetBrains.Annotations;

namespace fJson {
    /// <summary> Little JSON parsing/serialization library. </summary>
    public sealed class JsonObject : IDictionary<string, object>, ICloneable {
        readonly Dictionary<string, object> data = new Dictionary<string, object>();


        #region Parsing

        int index;
        string str;
        readonly StringBuilder stringParserBuffer;
        readonly List<object> arrayParserBuffer;


        /// <summary> Creates an empty JsonObject. </summary>
        public JsonObject() {
        }


        /// <summary> Creates a JsonObject from a serialized string. </summary>
        /// <param name="inputString"> Serialized JSON object to parse. </param>
        /// <exception cref="ArgumentNullException"> If inputString is null. </exception>
        public JsonObject( [NotNull] string inputString ) {
            if( inputString == null ) {
                throw new ArgumentNullException( "inputString" );
            }

            // parse the object
            stringParserBuffer = new StringBuilder();
            arrayParserBuffer = new List<object>();
            ReadJsonObject( inputString, 0 );

            // make sure there are no more tokens left
            Token token = FindNextToken();
            if( token != Token.None ) {
                ThrowUnexpected( token, "None" );
            }

            // free up memory that was used by the parser
            stringParserBuffer = null;
            arrayParserBuffer = null;
            str = null;
        }


        int ReadJsonObject( string inputString, int offset ) {
            str = inputString;
            index = offset;
            Token token = FindNextToken();
            if( token != Token.BeginObject ) {
                ThrowUnexpected( token, "BeginObject" );
            }

            index++;
            bool first = true;
            do {
                token = FindNextToken();

                if( token == Token.EndObject ) {
                    index++;
                    return index;
                }

                if( first ) {
                    first = false;
                } else if( token == Token.ValueSeparator ) {
                    index++;
                    token = FindNextToken();
                } else {
                    ThrowUnexpected( token, "EndObject or ValueSeparator" );
                }

                if( token != Token.String ) {
                    ThrowUnexpected( token, "String" );
                }

                string key = ReadString();
                token = FindNextToken();
                if( token != Token.NameSeparator ) {
                    ThrowUnexpected( token, "NameSeparator" );
                }
                index++;
                object value = ReadValue();
                Add( key, value );
            } while( token != Token.None );
            return index;
        }


        string ReadString() {
            stringParserBuffer.Length = 0;
            index++;
            int strLen = str.Length;

            for( int start = -1; index < strLen - 1; index++ ) {
                char c = str[index];

                if( c == '"' ) {
                    if( start != -1 && start != index ) {
                        if( stringParserBuffer.Length == 0 ) {
                            index++;
                            return str.Substring( start, index - start - 1 );
                        } else {
                            stringParserBuffer.Append( str, start, index - start );
                        }
                    }
                    index++;
                    return stringParserBuffer.ToString();
                }

                if( c == '\\' ) {
                    if( start != -1 && start != index ) {
                        stringParserBuffer.Append( str, start, index - start );
                        start = -1;
                    }
                    index++;
                    if( index >= strLen - 1 )
                        break;
                    switch( str[index] ) {
                        case '"':
                        case '/':
                        case '\\':
                            start = index;
                            continue;
                        case 'b':
                            stringParserBuffer.Append( '\b' );
                            continue;
                        case 'f':
                            stringParserBuffer.Append( '\f' );
                            continue;
                        case 'n':
                            stringParserBuffer.Append( '\n' );
                            continue;
                        case 'r':
                            stringParserBuffer.Append( '\r' );
                            continue;
                        case 't':
                            stringParserBuffer.Append( '\t' );
                            continue;
                        case 'u':
                            if( index >= strLen - 5 )
                                break;
                            uint c0 = ReadHexChar( str[index + 1], 0x1000 );
                            uint c1 = ReadHexChar( str[index + 2], 0x0100 );
                            uint c2 = ReadHexChar( str[index + 3], 0x0010 );
                            uint c3 = ReadHexChar( str[index + 4], 0x0001 );
                            stringParserBuffer.Append( (char)( c0 + c1 + c2 + c3 ) );
                            index += 4;
                            continue;
                    }
                }

                if( c < ' ' ) {
                    ThrowSerialization( "JsonObject: Unexpected character: 0x" +
                                        ( (int)c ).ToString( "X4", NumberFormatInfo.InvariantInfo ) + " at position " +
                                        index );
                }

                if( start == -1 ) {
                    start = index;
                }
            }
            throw new SerializationException( "JsonObject: Unexpected end of string." );
        }


        static uint ReadHexChar( char ch, uint multiplier ) {
            uint val = 0;
            if( ch >= '0' && ch <= '9' ) {
                val = (uint)( ch - '0' )*multiplier;
            } else if( ch >= 'A' && ch <= 'F' ) {
                val = (uint)( ( ch - 'A' ) + 10 )*multiplier;
            } else if( ch >= 'a' && ch <= 'f' ) {
                val = (uint)( ( ch - 'a' ) + 10 )*multiplier;
            } else {
                ThrowSerialization( "JsonObject: Incorrectly specified Unicode entity." );
            }
            return val;
        }


        object ReadValue() {
            Token token = FindNextToken();
            switch( token ) {
                case Token.BeginObject:
                    JsonObject newObj = new JsonObject();
                    index = newObj.ReadJsonObject( str, index );
                    return newObj;

                case Token.String:
                    return ReadString();

                case Token.Number:
                    return ReadNumber();

                case Token.Null:
                    if( index >= str.Length - 4 || str[index + 1] != 'u' || str[index + 2] != 'l' ||
                        str[index + 3] != 'l' ) {
                        ThrowSerialization( "JsonObject: Expected 'null' at position " + index );
                    }
                    index += 4;
                    return null;

                case Token.True:
                    if( index >= str.Length - 4 || str[index + 1] != 'r' || str[index + 2] != 'u' ||
                        str[index + 3] != 'e' ) {
                        ThrowSerialization( "JsonObject: Expected 'true' at position " + index );
                    }
                    index += 4;
                    return true;

                case Token.False:
                    if( index >= str.Length - 5 || str[index + 1] != 'a' || str[index + 2] != 'l' ||
                        str[index + 3] != 's' || str[index + 4] != 'e' ) {
                        ThrowSerialization( "JsonObject: Expected 'false' at position " + index );
                    }
                    index += 5;
                    return false;

                case Token.BeginArray:
                    arrayParserBuffer.Clear();
                    index++;
                    bool first = true;
                    while( true ) {
                        Token nextToken = FindNextToken();
                        if( nextToken == Token.EndArray )
                            break;
                        if( first ) {
                            first = false;
                        } else if( nextToken == Token.ValueSeparator ) {
                            index++;
                        } else {
                            ThrowUnexpected( nextToken, "ValueSeparator" );
                        }
                        arrayParserBuffer.Add( ReadValue() );
                    }
                    index++;
                    return arrayParserBuffer.ToArray();

                default:
                    ThrowUnexpected( token, "any value token" );
                    return null; // unreachable -- exception thrown above
            }
        }


        object ReadNumber() {
            long val = 1;
            double doubleVal = Double.NaN;
            bool first = true;
            bool negate = false;
            int strLen = str.Length;

            // Parse sign
            char c = str[index];
            if( str[index] == '-' ) {
                c = str[++index];
                negate = true;
            }

            // Parse integer part
            while( index < strLen ) {
                if( first ) {
                    if( c == '0' ) {
                        val = 0;
                        c = str[++index];
                        break;
                    } else if( c >= '1' && c <= '9' ) {
                        val = ( c - '0' );
                    }
                } else if( c >= '0' && c <= '9' ) {
                    val *= 10;
                    val += ( c - '0' );
                } else {
                    break;
                }
                first = false;
                c = str[++index];
            }
            if( index >= strLen ) {
                ThrowSerialization( "JsonObject: Unexpected end of a number (before decimal point) at position " + index );
            }

            // Parse fractional part (if present)
            if( c == '.' ) {
                c = str[++index];
                double fraction = 0;
                int multiplier = 10;
                first = true;
                while( index < strLen ) {
                    if( c >= '0' && c <= '9' ) {
                        fraction += ( c - '0' )/(double)multiplier;
                        multiplier *= 10;
                    } else if( first ) {
                        ThrowSerialization(
                            "JsonObject: Expected at least one digit after the decimal point at position " + index );
                    } else {
                        break;
                    }
                    c = str[++index];
                    first = false;
                }
                if( index >= strLen ) {
                    ThrowSerialization( "JsonObject: Unexpected end of a number (after decimal point) at position " +
                                        index );
                }
                doubleVal = val + fraction;
                // Negate (if needed)
                if( negate )
                    doubleVal = -doubleVal;

            } else {
                if( negate )
                    val = -val;
            }

            // Parse exponent (if present)
            if( c == 'e' || c == 'E' ) {
                int exponent = 1;
                negate = false;

                // Exponent sign
                c = str[++index];
                switch( c ) {
                    case '-':
                        negate = true;
                        c = str[++index];
                        break;
                    case '+':
                        c = str[++index];
                        break;
                }

                // Exponent value
                first = true;
                while( index < strLen ) {
                    if( first ) {
                        if( c == '0' ) {
                            exponent = 0;
                            index++;
                            break;
                        } else if( c >= '1' && c <= '9' ) {
                            exponent = ( c - '0' );
                        } else {
                            ThrowSerialization( "JsonObject: Unexpected character in exponent at position " + index );
                        }
                    } else if( c >= '0' && c <= '9' ) {
                        exponent *= 10;
                        exponent += ( c - '0' );
                    } else {
                        break;
                    }
                    first = false;
                    c = str[++index];
                }
                if( index >= strLen ) {
                    ThrowSerialization( "JsonObject: Unexpected end of a number (exponent) at position " + index );
                }

                // Apply the exponent
                if( negate ) {
                    exponent = -exponent;
                }
                if( Double.IsNaN( doubleVal ) ) {
                    doubleVal = val;
                }
                doubleVal *= Math.Pow( 10, exponent );
            }

            // Return value in appropriate format
            if( !Double.IsNaN( doubleVal ) ) {
                return doubleVal;
            } else if( val >= Int32.MinValue && val <= Int32.MaxValue ) {
                return (int)val;
            } else {
                return val;
            }
        }


        Token FindNextToken() {
            int strLen = str.Length;
            if( index >= strLen ) {
                return Token.None;
            }

            char c = str[index];
            while( c == ' ' || c == '\t' || c == '\r' || c == '\n' ) {
                index++;
                if( index >= strLen ) {
                    return Token.None;
                }
                c = str[index];
            }

            switch( c ) {
                case '{':
                    return Token.BeginObject;
                case '}':
                    return Token.EndObject;
                case '[':
                    return Token.BeginArray;
                case ']':
                    return Token.EndArray;
                case 'n':
                    return Token.Null;
                case 't':
                    return Token.True;
                case 'f':
                    return Token.False;
                case ':':
                    return Token.NameSeparator;
                case ',':
                    return Token.ValueSeparator;
                case '"':
                    return Token.String;
                case '-':
                case '0':
                case '1':
                case '2':
                case '3':
                case '4':
                case '5':
                case '6':
                case '7':
                case '8':
                case '9':
                    return Token.Number;
                default:
                    return Token.Error;
            }
        }


        [ContractAnnotation( "=> void" )]
        void ThrowUnexpected( Token given, string expected ) {
            throw new SerializationException( "JsonObject: Unexpected token " + given + ", expecting " + expected +
                                              " at position " + index );
        }


        [ContractAnnotation( "=> void" )]
        static void ThrowSerialization( string message ) {
            throw new SerializationException( message );
        }

        #endregion


        #region Serialization

        /// <summary> Serializes this JsonObject with default settings. </summary>
        [NotNull]
        public string Serialize() {
            return new JsonSerializer().Serialize( this );
        }


        /// <summary> Serializes this JsonObject with custom indentation. </summary>
        /// <param name="indent"> Number of spaces to use for indentation.
        /// If zero or positive, padding and line breaks are added.
        /// If negative, serialization is done as compactly as possible. </param>
        [NotNull]
        public string Serialize( int indent ) {
            return new JsonSerializer( indent ).Serialize( this );
        }

        #endregion


        #region Has/Get/TryGet shortcuts

        // ==== non-cast ====
        public bool Has( [NotNull] string key ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            return data.ContainsKey( key );
        }


        public bool HasNull( [NotNull] string key ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            object boxedVal;
            if( !data.TryGetValue( key, out boxedVal ) ) {
                return false;
            }
            return ( boxedVal == null );
        }


        public object Get( [NotNull] string key ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            return data[key];
        }


        public bool TryGet( [NotNull] string key, [CanBeNull] out object val ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            return data.TryGetValue( key, out val );
        }


        // ==== strings ====
        public string GetString( [NotNull] string key ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            return (string)data[key];
        }


        public bool TryGetString( [NotNull] string key, out string val ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            object boxedVal;
            if( !data.TryGetValue( key, out boxedVal ) ) {
                val = null;
                return false;
            }
            val = ( boxedVal as string );
            return ( val != null );
        }


        public bool TryGetStringOrNull( [NotNull] string key, [CanBeNull] out string val ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            val = null;
            object boxedVal;
            if( !data.TryGetValue( key, out boxedVal ) ) {
                return false;
            }
            if( boxedVal == null ) {
                return true;
            }
            val = ( boxedVal as string );
            return ( val != null );
        }


        public bool HasString( [NotNull] string key ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            object boxedVal;
            if( !data.TryGetValue( key, out boxedVal ) ) {
                return false;
            }
            return ( boxedVal as string != null );
        }


        public bool HasStringOrNull( [NotNull] string key ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            object boxedVal;
            if( !data.TryGetValue( key, out boxedVal ) ) {
                return false;
            }
            return ( boxedVal == null ) || ( boxedVal as string != null );
        }


        // ==== integers ====
        public int GetInt( [NotNull] string key ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            return (int)data[key];
        }


        public bool TryGetInt( [NotNull] string key, out int val ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            val = 0;
            object boxedVal;
            if( !data.TryGetValue( key, out boxedVal ) ) {
                return false;
            }
            if( !( boxedVal is int ) )
                return false;
            val = (int)boxedVal;
            return true;
        }


        public bool HasInt( [NotNull] string key ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            object boxedVal;
            if( !data.TryGetValue( key, out boxedVal ) ) {
                return false;
            }
            return ( boxedVal is int );
        }


        // ==== longs ====
        public long GetLong( [NotNull] string key ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            return (long)data[key];
        }


        public bool TryGetLong( [NotNull] string key, out long val ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            val = 0;
            object boxedVal;
            if( !data.TryGetValue( key, out boxedVal ) ) {
                return false;
            }
            if( !( boxedVal is long ) )
                return false;
            val = (long)boxedVal;
            return true;
        }


        public bool HasLong( [NotNull] string key ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            object boxedVal;
            if( !data.TryGetValue( key, out boxedVal ) ) {
                return false;
            }
            return ( boxedVal is long );
        }


        // ==== double ====
        public double GetDouble( [NotNull] string key ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            return (double)data[key];
        }


        public bool TryGetDouble( [NotNull] string key, out double val ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            val = 0;
            object boxedVal;
            if( !data.TryGetValue( key, out boxedVal ) ) {
                return false;
            }
            if( !( boxedVal is double ) )
                return false;
            val = (double)boxedVal;
            return true;
        }


        public bool HasDouble( [NotNull] string key ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            object boxedVal;
            if( !data.TryGetValue( key, out boxedVal ) ) {
                return false;
            }
            return ( boxedVal is double );
        }


        // ==== boolean ====
        public bool GetBool( [NotNull] string key ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            return (bool)data[key];
        }


        public bool TryGetBool( [NotNull] string key, out bool val ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            val = false;
            object boxedVal;
            if( !data.TryGetValue( key, out boxedVal ) ) {
                return false;
            }
            if( !( boxedVal is bool ) )
                return false;
            val = (bool)boxedVal;
            return true;
        }


        public bool HasBool( [NotNull] string key ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            object boxedVal;
            if( !data.TryGetValue( key, out boxedVal ) ) {
                return false;
            }
            return ( boxedVal is bool );
        }


        // ==== JsonObject ====
        [CanBeNull]
        public JsonObject GetObject( [NotNull] string key ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            return (JsonObject)data[key];
        }


        public bool TryGetObject( [NotNull] string key, out JsonObject val ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            object boxedVal;
            if( !data.TryGetValue( key, out boxedVal ) ) {
                val = null;
                return false;
            }
            val = ( boxedVal as JsonObject );
            return ( val != null );
        }


        public bool TryGetObjectOrNull( [NotNull] string key, [CanBeNull] out JsonObject val ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            val = null;
            object boxedVal;
            if( !data.TryGetValue( key, out boxedVal ) ) {
                return false;
            }
            if( boxedVal == null ) {
                return true;
            }
            val = ( boxedVal as JsonObject );
            return ( val != null );
        }


        public bool HasObject( [NotNull] string key ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            object boxedVal;
            if( !data.TryGetValue( key, out boxedVal ) ) {
                return false;
            }
            return ( boxedVal as JsonObject != null );
        }


        public bool HasObjectOrNull( [NotNull] string key ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            object boxedVal;
            if( !data.TryGetValue( key, out boxedVal ) ) {
                return false;
            }
            return ( boxedVal == null ) || ( boxedVal as JsonObject != null );
        }


        // ==== Array ====
        [CanBeNull]
        public T[] GetArray<T>( [NotNull] string key ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            if( data[key] is T[] ) {
                return (T[])data[key];
            } else {
                return ( (object[])data[key] ).Cast<T>().ToArray();
            }
        }


        public bool TryGetArray<T>( [NotNull] string key, out T[] val ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            object boxedVal;
            if( !data.TryGetValue( key, out boxedVal ) ) {
                val = null;
                return false;
            }
            try {
                val = GetArray<T>( key );
                return true;
            } catch( InvalidCastException ) {
                val = null;
                return false;
            }
        }


        public bool TryGetArrayOrNull<T>( [NotNull] string key, [CanBeNull] out T[] val ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            val = null;
            object boxedVal;
            if( !data.TryGetValue( key, out boxedVal ) ) {
                return false;
            }
            if( boxedVal == null ) {
                return true;
            }
            try {
                val = GetArray<T>( key );
                return true;
            } catch( InvalidCastException ) {
                val = null;
                return false;
            }
        }


        public bool HasArray( [NotNull] string key ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            object boxedVal;
            if( !data.TryGetValue( key, out boxedVal ) ) {
                return false;
            }
            return ( boxedVal as object[] != null );
        }


        public bool HasArrayOrNull( [NotNull] string key ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            object boxedVal;
            if( !data.TryGetValue( key, out boxedVal ) ) {
                return false;
            }
            return ( boxedVal == null ) || ( boxedVal as object[] != null );
        }


        // ==== Enum ====
        public T GetEnum<T>( [NotNull] string key ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            return (T)Enum.Parse( typeof( T ), data[key].ToString() );
        }


        public bool TryGetEnum<T>( [NotNull] string key, bool ignoreCase, out T val ) where T : struct {
            if( key == null )
                throw new ArgumentNullException( "key" );
            object boxedVal;
            if( !data.TryGetValue( key, out boxedVal ) ) {
                val = default( T );
                return false;
            }
            return Enum.TryParse( boxedVal.ToString(), ignoreCase, out val );
        }


        public bool HasEnum<T>( [NotNull] string key, bool ignoreCase ) where T : struct {
            if( key == null )
                throw new ArgumentNullException( "key" );
            T val;
            object boxedVal;
            if( !data.TryGetValue( key, out boxedVal ) ) {
                return false;
            }
            return Enum.TryParse( boxedVal.ToString(), ignoreCase, out val );
        }

        #endregion


        #region IDictionary / ICollection / ICloneable members

        /// <summary> Creates a JsonObject from an existing JsonObject or string-object dictionary. </summary>
        public JsonObject( IEnumerable<KeyValuePair<string, object>> other ) {
            foreach( var kvp in other ) {
                Add( kvp );
            }
        }


        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() {
            return data.GetEnumerator();
        }


        IEnumerator IEnumerable.GetEnumerator() {
            return data.GetEnumerator();
        }


        public void Add( KeyValuePair<string, object> item ) {
            Add( item.Key, item.Value );
        }


        public void Clear() {
            data.Clear();
        }


        bool ICollection<KeyValuePair<string, object>>.Contains( KeyValuePair<string, object> item ) {
            return ( data as ICollection<KeyValuePair<string, object>> ).Contains( item );
        }


        void ICollection<KeyValuePair<string, object>>.CopyTo( KeyValuePair<string, object>[] array, int arrayIndex ) {
            ( data as ICollection<KeyValuePair<string, object>> ).CopyTo( array, arrayIndex );
        }


        bool ICollection<KeyValuePair<string, object>>.Remove( KeyValuePair<string, object> item ) {
            return ( data as ICollection<KeyValuePair<string, object>> ).Remove( item );
        }


        public int Count {
            get {
                return data.Count;
            }
        }


        bool ICollection<KeyValuePair<string, object>>.IsReadOnly {
            get {
                return false;
            }
        }


        public bool ContainsKey( string key ) {
            return data.ContainsKey( key );
        }


        public void Add( [NotNull] string key, JsonObject obj ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            data.Add( key, obj );
        }


        public void Add( [NotNull] string key, int obj ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            data.Add( key, obj );
        }


        public void Add( [NotNull] string key, long obj ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            data.Add( key, obj );
        }


        public void Add( [NotNull] string key, double obj ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            data.Add( key, obj );
        }


        public void Add( [NotNull] string key, bool obj ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            data.Add( key, obj );
        }


        public void Add( [NotNull] string key, string obj ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            data.Add( key, obj );
        }


        public void Add( [NotNull] string key, Array obj ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            data.Add( key, obj );
        }


        public void Add( string key, object obj ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            if( obj == null || obj is JsonObject || obj is Array || obj is string || obj is int || obj is long ||
                obj is double || obj is bool ) {
                data.Add( key, obj );
            } else if( obj is sbyte ) {
                data.Add( key, (int)(sbyte)obj );
            } else if( obj is byte ) {
                data.Add( key, (int)(byte)obj );
            } else if( obj is short ) {
                data.Add( key, (int)(short)obj );
            } else if( obj is ushort ) {
                data.Add( key, (int)(ushort)obj );
            } else if( obj is uint ) {
                data.Add( key, (long)(uint)obj );
            } else if( obj is float ) {
                data.Add( key, (double)(float)obj );
            } else if( obj is decimal ) {
                data.Add( key, (double)(decimal)obj );
            } else if( obj.GetType().IsEnum ) {
                Add( key, obj.ToString() );
            } else {
                throw new ArgumentException( "JsonObject: Unacceptable value type." );
            }
        }


        public bool Remove( string key ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            return data.Remove( key );
        }


        bool IDictionary<string, object>.TryGetValue( string key, out object value ) {
            if( key == null )
                throw new ArgumentNullException( "key" );
            return data.TryGetValue( key, out value );
        }


        public object this[ string key ] {
            get {
                if( key == null )
                    throw new ArgumentNullException( "key" );
                return data[key];
            }
            set {
                if( key == null )
                    throw new ArgumentNullException( "key" );
                data[key] = value;
            }
        }


        public ICollection<string> Keys {
            get {
                return data.Keys;
            }
        }


        public ICollection<object> Values {
            get {
                return data.Values;
            }
        }


        public object Clone() {
            return new JsonObject( this );
        }

        #endregion
    }
}