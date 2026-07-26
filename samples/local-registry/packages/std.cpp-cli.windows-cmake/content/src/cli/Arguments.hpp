#pragma once

#include <stdexcept>
#include <string>
#include <vector>

namespace cli
{
struct ParsedArguments
{
    bool showHelp = false;
    bool showVersion = false;
    std::vector<std::string> positional;
};

class ArgumentError final : public std::runtime_error
{
public:
    using std::runtime_error::runtime_error;
};

ParsedArguments parseArguments(int argc, char* argv[]);
}
