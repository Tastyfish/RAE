# RAE (Room Adventure Engine)
This is a text-based adventure game engine with auto-complete and color highlighting to make the experience more natural than most text-based adventure engines.

## Use of program
```
rae [files ...] [-s] [-v] [--name=...]
```
* The RAD files are passed into RAE for compilation.
  * The game should generally be defined in the first file.
  * Likewise, the first file name is the default internal game name if one is not provided by `--name`.
* `-s` specifies that the compiled result should be saved to `[name].exe`.
  The game is executed either way.
* `-v` specifies that verbose debugging information should be provided during compilation.

## RAD

The engine uses a custom language I've called RAD (Room Adventure Definition). It's very similar to other ALGOL/C-based languages but uses colons more and parenthesis less.

The following C# code:
```c#
void print_list(List<int> l) {
    for(int i = 0; i < l.Count; i++)
        Console.WriteLine("l[" + i + "] = " + l[i]);
}
```

would look like in RAD:
```c#
fn void printl(List#int l) {
		for int i = 0; i < l.count; i++:
			"l[" + i + "] = " + l[i];
}
```

Note the use of a colon after the for and the lack of parenthesis around its parameters.
Also note that the console printing is achieved by a string expression being used as a statement.

Dialog can be written fairly naturally with a `speaker: dialog` syntax:

```c#
npc chef "Mr Cook" {
    on talk {
        this: "Go away, " + player + "!";
        player: "OK...";
    }
}
```

Note that verbs and events are generally handled via `on x: statement` or `on x { block }`.
Classes can also have normal methods via `fn` as before, but event and verb handlers are special.

In particular, certain key properties of most game objects, such as their name and the verb handlers, are wrapped in a _State_ and can be swapped out at any time.

States are defined via `state X { block of differences }`, set via `to state` or `object to state`, and checked via `(object) is state` or `(object) isnt state`.

```c#
item locket a "closed locket" {
    aka { "locket" };
    
    on examine {
        describe();
        "It appears to be made of gold.";
    }

    state open {
        name = "opened locket";
        
        on close: to default;
        
        on look: "There's a picture of a lovely couple inside.";
    }
    
    on open: to open;
    
    on look: "The locket looks like it could be "+colorize("opened", "yellow")+".";
}
```

It's important note that `on` statements defined _after_ an explicit `state` definition will _only_ be placed in the default state, whereas ones before the first `state` will be in all following states. This way, a specific state can choose to not handle a verb at all.
