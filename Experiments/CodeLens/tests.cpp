#define CATCH_CONFIG_MAIN
#include <catch.hpp>
#include <fstream>
#include <filesystem>

TEST_CASE("CodeLens passes",
          "[codelens]")
{
    volatile int answer = 42; // Set a native breakpoint here.
    REQUIRE(answer == 42);
}

TEST_CASE("CodeLens toggles failure", "[codelens]")
{
    const auto marker = std::filesystem::path(__FILE__).parent_path() / "fail.flag";
    REQUIRE_FALSE(std::ifstream(marker).good());
}
