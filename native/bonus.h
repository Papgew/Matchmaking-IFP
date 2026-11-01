#ifndef MATCHMAKING_BONUS_H
#define MATCHMAKING_BONUS_H

#if defined(_WIN32)
#define MATCH_API __declspec(dllexport)
#else
#define MATCH_API __attribute__((visibility("default")))
#endif

#ifdef __cplusplus
extern "C" {
#endif
MATCH_API int mm_bonus(int kills, int deaths, int weight);
#ifdef __cplusplus
}
#endif
#endif
