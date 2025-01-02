### URL Shortener Architecture with .NET Aspire

---
```
POST /shorten
{
  "https://www.google.com"
}
```
returns `dSNsKmM`  

---

```
GET /urls
```
returns
```
[
    {
        "shortCode": "dSNsKmM",
        "originalUrl": "https://www.google.com",
        "createdAt": "2025-01-02T19:57:52.879063Z"
    }
]
```
---
```
GET /dSNsKmM
```
returns
```
<!DOCTYPE html>
<html class="scroll-smooth">

<head>
...
</head>

<body>
...
</body>

</html>
```
**note: check successful Metrics**  
`https://localhost:17083/metrics/resource/urlshortener-api?meter=UrlShortener.Api&instrument=url_shortener.redirects&duration=5&view=Graph`

---
```
GET /dSNsKmM
```
returns: `404`  
**note: check failed Metrics**  
`https://localhost:17083/metrics/resource/urlshortener-api?meter=UrlShortener.Api&instrument=url_shortener.failed_redirects&duration=5&view=Graph`
