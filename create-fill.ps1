dir templates\*.schematic | select-object -expandProperty fullname | sort  | % {$i=1}{ src2\ConsoleApplication2\bin\Debug\ConsoleApplication2.exe $_ "$_.fill" 20 3 $(75*$i);$i++}