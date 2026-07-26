#include "Arguments.hpp"

#include <string_view>

namespace cli
{
ParsedArguments parseArguments(int argc, char* argv[])
{
    ParsedArguments result;
    for (int index = 1; index < argc; ++index)
    {
        const std::string_view argument = argv[index];
        if (argument == "--help" || argument == "-h")
        {
            result.showHelp = true;
        }
        else if (argument == "--version")
        {
            result.showVersion = true;
        }
        else if (argument.starts_with('-'))
        {
            throw ArgumentError("unknown option: " + std::string(argument));
        }
        else
        {
            result.positional.emplace_back(argument);
        }
    }

    return result;
}
}
