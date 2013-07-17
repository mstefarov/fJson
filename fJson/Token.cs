namespace fJson {
    enum Token {
        None,
        Error,

        BeginObject,
        EndObject,
        BeginArray,
        EndArray,
        NameSeparator,
        ValueSeparator,
        Null,
        True,
        False,
        String,
        Number
    }
}