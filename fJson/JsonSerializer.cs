using System;
using System.Globalization;
using System.Runtime.Serialization;
using System.Text;
using JetBrains.Annotations;

namespace fJson {
    sealed class JsonSerializer {
        readonly int indent = 2;
        readonly bool compact;
        int indentLevel;
        readonly StringBuilder sb = new StringBuilder();

        public JsonSerializer() {
        }


        public JsonSerializer( int indent ) {
            this.indent = indent;
            compact = ( indent < 0 );
        }


        public string Serialize( [NotNull] JsonObject obj ) {
            sb.Clear();
            indentLevel = 0;
            SerializeInternal( obj );
            return sb.ToString();
        }


        void Indent() {
            sb.Append( '\r' ).Append( '\n' ).Append( ' ', indentLevel*indent );
        }


        void SerializeInternal( [NotNull] JsonObject obj ) {
            sb.Append( '{' );
            if( obj.Count > 0 ) {
                indentLevel++;
                bool first = true;
                foreach( var kvp in obj ) {
                    if( first ) {
                        first = false;
                    } else {
                        sb.Append( ',' );
                    }
                    if( !compact )
                        Indent();
                    WriteString( kvp.Key );
                    sb.Append( ':' );
                    if( !compact )
                        sb.Append( ' ' );
                    WriteValue( kvp.Value );
                }
                indentLevel--;
                if( !compact ) {
                    Indent();
                }
            }
            sb.Append( '}' );
        }


        void WriteValue( [CanBeNull] object obj ) {
            JsonObject asObject;
            string asString;
            Array asArray;
            if( obj == null ) {
                sb.Append( "null" );
            } else if( ( asObject = obj as JsonObject ) != null ) {
                SerializeInternal( asObject );
            } else if( obj is int ) {
                sb.Append( ( (int)obj ).ToString( NumberFormatInfo.InvariantInfo ) );
            } else if( obj is double ) {
                sb.Append( ( (double)obj ).ToString( NumberFormatInfo.InvariantInfo ) );
            } else if( ( asString = obj as string ) != null ) {
                WriteString( asString );
            } else if( ( asArray = obj as Array ) != null ) {
                WriteArray( asArray );
            } else if( obj is long ) {
                sb.Append( ( (long)obj ).ToString( NumberFormatInfo.InvariantInfo ) );
            } else if( obj is bool ) {
                if( (bool)obj ) {
                    sb.Append( "true" );
                } else {
                    sb.Append( "false" );
                }
            } else {
                throw new SerializationException( "JsonObject: Non-serializable object found in the collection." );
            }
        }


        void WriteString( [NotNull] string str ) {
            sb.Append( '"' );
            int runIndex = -1;

            for( var i = 0; i < str.Length; i++ ) {
                var c = str[i];

                if( c >= ' ' && c < 128 && c != '\"' && c != '\\' ) {
                    if( runIndex == -1 ) {
                        runIndex = i;
                    }
                    continue;
                }

                if( runIndex != -1 ) {
                    sb.Append( str, runIndex, i - runIndex );
                    runIndex = -1;
                }

                sb.Append( '\\' );
                switch( c ) {
                    case '\b':
                        sb.Append( 'b' );
                        break;
                    case '\f':
                        sb.Append( 'f' );
                        break;
                    case '\n':
                        sb.Append( 'n' );
                        break;
                    case '\r':
                        sb.Append( 'r' );
                        break;
                    case '\t':
                        sb.Append( 't' );
                        break;
                    case '"':
                    case '\\':
                        sb.Append( c );
                        break;
                    default:
                        sb.Append( 'u' );
                        sb.Append( ( (int)c ).ToString( "X4", NumberFormatInfo.InvariantInfo ) );
                        break;
                }
            }

            if( runIndex != -1 ) {
                sb.Append( str, runIndex, str.Length - runIndex );
            }

            sb.Append( '\"' );
        }


        void WriteArray( [NotNull] Array array ) {
            sb.Append( '[' );
            bool first = true;
            for( int i = 0; i < array.Length; i++ ) {
                if( first ) {
                    first = false;
                } else {
                    sb.Append( ',' );
                    if( !compact )
                        sb.Append( ' ' );
                }
                WriteValue( array.GetValue( i ) );
            }
            sb.Append( ']' );
        }
    }
}