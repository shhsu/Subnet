#!/bin/sh

# downloaded from https://cloud.google.com/compute/docs/faq#where_can_i_find_product_name_short_ip_ranges

set -- $(dig -t txt +short _cloud-netblocks.googleusercontent.com)

included="" ip4=""
while [ $# -gt 0 ]; do
  k="${1%%:*}" v="${1#*:}"
  case "$k" in
    include)
      # only include once
      if [ "${included% $v *}" = "${included}" ]; then
        set -- "$@" $(dig -t txt +short "$v")
        included=" $v $included"
      fi
      ;;
    ip4) ip4="$v $ip4" ;;
  esac
  shift
done

for i in $ip4; do
  echo "$i"
done
