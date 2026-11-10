import { useState } from 'react'
import { useMutation, useQuery, useQueryClient, type UseMutationResult } from '@tanstack/react-query'
import { ApiError } from '../api/flights'
import {
  createOwner,
  createSavedLocation,
  deleteSavedLocation,
  fetchSavedLocations,
  type SavedLocation,
  type SavedLocationInput,
} from '../api/locations'
import { clearOwnerToken, readOwnerToken, writeOwnerToken } from '../api/ownerToken'
import { shouldRetry } from '../api/retryPolicy'

/** Every saved-locations query starts with this, so one invalidation refreshes them all. */
const QUERY_KEY = ['saved-locations'] as const

export interface SavedLocations {
  locations: SavedLocation[]
  /** Loading the list failed (not "you have none"). */
  loadError: Error | null
  save: UseMutationResult<SavedLocation, Error, SavedLocationInput>
  remove: UseMutationResult<void, Error, string>
}

const isUnauthorized = (error: unknown) => error instanceof ApiError && error.status === 401

/**
 * This browser's saved locations, and how to add and delete them.
 *
 * Reading is a query, like the flights. Changing is a **mutation**: TanStack Query runs it on demand (`mutate`),
 * tracks it (`isPending`, `error`) and, once it succeeds, we *invalidate* the list, which makes the query fetch it
 * again. The server stays the one source of truth instead of us patching the cached list by hand.
 *
 * The owner token is created lazily, by the first save: visitors who never save leave nothing in the database.
 * A token the API no longer accepts (401: the database was reset) is forgotten, and the next save starts afresh.
 */
export function useSavedLocations(): SavedLocations {
  const queryClient = useQueryClient()
  const [token, setToken] = useState(() => readOwnerToken())

  const forgetToken = () => {
    clearOwnerToken()
    setToken(null)
  }

  /** A new owner, remembered in this browser. */
  const becomeNewOwner = async (): Promise<string> => {
    const created = await createOwner()
    writeOwnerToken(created)
    setToken(created)
    return created
  }

  const query = useQuery<SavedLocation[], Error>({
    // The token is part of the key: a different owner is different data.
    queryKey: [...QUERY_KEY, token],
    queryFn: async ({ signal }) => {
      try {
        return await fetchSavedLocations(token!, signal)
      } catch (error) {
        if (isUnauthorized(error)) {
          forgetToken() // set here, where the 401 arrives, like useFlights' rate limit: no effect needed
          return []
        }
        throw error
      }
    },
    // No token, no saved locations: nothing to ask the server.
    enabled: token !== null,
    retry: shouldRetry,
    // Only this browser changes them (and it refetches after each change), so no polling and no refetch on every
    // window focus.
    staleTime: Infinity,
  })

  const save = useMutation<SavedLocation, Error, SavedLocationInput>({
    mutationFn: async (input) => {
      const current = token ?? (await becomeNewOwner())
      try {
        return await createSavedLocation(current, input)
      } catch (error) {
        if (!isUnauthorized(error)) {
          throw error
        }
        // The stored token no longer works: start again as a new owner rather than fail the save.
        return createSavedLocation(await becomeNewOwner(), input)
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: QUERY_KEY }),
  })

  const remove = useMutation<void, Error, string>({
    mutationFn: async (id) => {
      if (token === null) {
        return
      }
      try {
        await deleteSavedLocation(token, id)
      } catch (error) {
        if (!isUnauthorized(error)) {
          throw error
        }
        forgetToken() // its locations are unreachable anyway
      }
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: QUERY_KEY }),
  })

  return {
    locations: token === null ? [] : (query.data ?? []),
    loadError: query.error,
    save,
    remove,
  }
}
